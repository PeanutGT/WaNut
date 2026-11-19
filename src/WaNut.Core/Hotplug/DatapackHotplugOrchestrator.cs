using System.Diagnostics;
using WaNut.Core.Downloads;
using WaNut.Core.Hotplug.Preflight;
using WaNut.Core.Hotplug.Readiness;
using WaNut.Core.Storage;

namespace WaNut.Core.Hotplug;

/// <summary>
/// Hot-plugs data packs into a server running on a VPS, one batch at a time:
/// <see cref="HotplugState.PreFlightCheck"/> → <see cref="HotplugState.UploadingStaging"/> →
/// <see cref="HotplugState.VerifyingRemote"/> → <see cref="HotplugState.AtomicSwap"/> →
/// <see cref="HotplugState.ApplyingAndReloading"/> → <see cref="HotplugState.Completed"/>, or
/// <see cref="HotplugState.RolledBack"/> when the swap or the check after /reload fails.
/// Nothing in the world's datapacks folder changes before the swap, and the run can only be cancelled before it;
/// from the swap on it always finishes (or undoes) what it started. Every run that got the lock writes an audit line.
/// </summary>
public sealed class DatapackHotplugOrchestrator(HotplugConnection connection, HotplugHooks? hooks = null, TimeProvider? time = null)
{
    /// <summary>How long the console gets to answer <c>datapack list enabled</c> by default.</summary>
    public static readonly TimeSpan DefaultConsoleTimeout = TimeSpan.FromSeconds(15);

    private readonly HotplugHooks _hooks = hooks ?? HotplugHooks.None;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _oneRun = new(1, 1);

    /// <summary>How long the console gets to answer <c>datapack list enabled</c>.</summary>
    public TimeSpan ConsoleTimeout { get; init; } = DefaultConsoleTimeout;

    public HotplugState State { get; private set; }

    public bool IsBusy => _oneRun.CurrentCount == 0;

    /// <summary>
    /// Waits for <paramref name="source"/> to hold a complete batch (<see cref="ReadinessGate"/>), showing
    /// <see cref="HotplugState.WaitingForData"/> meanwhile. Cancel to stop waiting; the state goes back to idle.
    /// </summary>
    public async Task<ReadinessResult> WaitForDataAsync(IReadinessSource source, ReadinessOptions? options = null, IProgress<HotplugProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Set(HotplugState.WaitingForData, progress);
        try
        {
            var gate = new ReadinessGate(_time);
            return await gate.WaitAsync(source, options, new SyncProgress<ReadinessStatus>(s => progress?.Report(new HotplugProgress(HotplugState.WaitingForData, Readiness: s))), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (State == HotplugState.WaitingForData)
            {
                State = HotplugState.Idle;
            }
        }
    }

    /// <exception cref="InvalidOperationException">Another run of this orchestrator is still going.</exception>
    /// <exception cref="OperationCanceledException">Cancelled before the swap. Nothing in the world changed and the uploads are cleaned up.</exception>
    public async Task<HotplugReport> RunAsync(HotplugRequest request, IProgress<HotplugProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_oneRun.Wait(0))
        {
            throw new InvalidOperationException("A hot-plug is already running.");
        }

        try
        {
            var report = await RunCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
            Set(report.Outcome, progress, report.Message);
            return report;
        }
        catch
        {
            State = HotplugState.Idle;
            throw;
        }
        finally
        {
            _oneRun.Release();
        }
    }

    private async Task<HotplugReport> RunCoreAsync(HotplugRequest request, IProgress<HotplugProgress>? progress, CancellationToken cancellationToken)
    {
        // Pre-flight: everything about the packs themselves is checked on this PC before the VPS is touched.
        Set(HotplugState.PreFlightCheck, progress);
        var problem = CheckNames(request);
        if (problem is not null)
        {
            return Aborted(problem, []);
        }

        var expected = DatapackFormats.For(request.MinecraftVersion);
        var inspections = new List<DatapackInspection>();
        foreach (var path in request.Add)
        {
            progress?.Report(new HotplugProgress(HotplugState.PreFlightCheck, Path.GetFileName(path)));
            inspections.Add(await DatapackInspector.InspectAsync(path, expected, cancellationToken).ConfigureAwait(false));
        }

        if (inspections.Where(i => i.IsBlocked).ToList() is { Count: > 0 } blocked)
        {
            return Aborted(string.Join(" ", blocked.Select(BlockedReason)), inspections);
        }

        if (request.Confirm is { } confirm && inspections.Any(i => i.NeedsConfirmation)
            && !await confirm(inspections.Where(i => i.NeedsConfirmation).ToList(), cancellationToken).ConfigureAwait(false))
        {
            return Aborted("Cancelled after the pre-flight warnings.", inspections);
        }

        var workspace = await HotplugWorkspaceInitializer.InitializeAsync(connection.Runner, connection.Root, await LiveDatapacks.WorldFolderAsync(connection.Files, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        var acquired = await HotplugLock.TryAcquireAsync(connection.Runner, connection.Root, workspace, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!acquired.Acquired)
        {
            return Aborted("Another hot-plug is running on this VPS.", inspections) with { Holder = acquired.Holder };
        }

        await using var held = acquired.Lock!;
        var report = await RunLockedAsync(request, inspections, workspace, held, progress, cancellationToken).ConfigureAwait(false);
        await AuditAsync(workspace, report).ConfigureAwait(false);
        return report;
    }

    private async Task<HotplugReport> RunLockedAsync(
        HotplugRequest request, List<DatapackInspection> inspections, HotplugWorkspace workspace, HotplugLock held, IProgress<HotplugProgress>? progress, CancellationToken cancellationToken)
    {
        var files = connection.Files;
        var runner = connection.Runner;
        var root = connection.Root;
        var present = await ListPacksAsync(files, workspace.DatapacksFolder, cancellationToken).ConfigureAwait(false);
        var missing = request.Remove.Where(n => !present.Contains(n)).ToList();
        if (missing.Count > 0)
        {
            return Aborted($"Not in the datapacks folder: {string.Join(", ", missing)}.", inspections);
        }

        if (_hooks.Gate is { } gate && await gate.CheckAsync(connection.Console, cancellationToken).ConfigureAwait(false) is { } heldBack)
        {
            return Aborted(heldBack, inspections);
        }

        var stamp = _time.GetUtcNow();
        var steps = inspections.Select(i => new HotplugStep(
                HotplugStepKind.Put, i.FileName, $"{workspace.StagingFolder}/{i.FileName}", $"{workspace.DatapacksFolder}/{i.FileName}",
                $"{workspace.BackupFolder}/{HotplugBackups.NameFor(i.FileName, stamp)}"))
            .Concat(request.Remove.Select(n => new HotplugStep(
                HotplugStepKind.Retire, n, null, $"{workspace.DatapacksFolder}/{n}", $"{workspace.BackupFolder}/{HotplugBackups.NameFor(n, stamp)}")))
            .ToList();
        var uploads = steps.Where(s => s.Kind == HotplugStepKind.Put).SelectMany(s => new[] { s.Staged! + ".part", s.Staged! }).ToList();

        try
        {
            await UploadAndVerifyAsync(inspections, steps, held, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (HotplugUploadException e)
        {
            await CleanAsync(uploads).ConfigureAwait(false);
            return Aborted(e.Message, inspections);
        }
        catch (OperationCanceledException)
        {
            await CleanAsync(uploads).ConfigureAwait(false);
            throw;
        }

        if (!await held.RefreshAsync(cancellationToken).ConfigureAwait(false))
        {
            await CleanAsync(uploads).ConfigureAwait(false);
            return Aborted("The hot-plug lock was taken over by someone else; nothing was changed.", inspections);
        }

        // From here on the world changes, so the run finishes or undoes what it started even if the user cancels.
        var console = connection.Console;
        var retiring = steps.Where(s => s.Kind == HotplugStepKind.Retire).ToList();
        if (retiring.Count > 0)
        {
            // Only packs that were on get switched back on if the run is undone.
            var before = await ConsoleQuery.EnabledPacksAsync(console, ConsoleTimeout, CancellationToken.None).ConfigureAwait(false);
            retiring = before is null ? retiring : retiring.Where(s => before.Contains(HotplugNames.PackId(s.FileName))).ToList();
        }

        foreach (var step in retiring)
        {
            await console.SendCommandAsync("datapack disable " + HotplugNames.Quoted(step.FileName), CancellationToken.None).ConfigureAwait(false);
        }

        if (retiring.Count > 0)
        {
            // The list answer comes after the disables are done, since the console runs commands in order.
            var enabledNow = await ConsoleQuery.EnabledPacksAsync(console, ConsoleTimeout, CancellationToken.None).ConfigureAwait(false);
            if (enabledNow is not null && retiring.FirstOrDefault(s => enabledNow.Contains(HotplugNames.PackId(s.FileName))) is { } stuck)
            {
                await ReEnableAsync(retiring).ConfigureAwait(false);
                await CleanAsync(uploads).ConfigureAwait(false);
                return Aborted($"The server didn't disable {stuck.FileName}; nothing was changed.", inspections);
            }
        }

        Set(HotplugState.AtomicSwap, progress);
        var clock = Stopwatch.StartNew();
        var swap = await runner.RunAsync(HotplugSwapCommands.Swap(root, steps), cancellationToken: CancellationToken.None).ConfigureAwait(false);
        var swapped = ParseSwap(swap.Output, steps);
        if (swap.ExitStatus != 0)
        {
            var why = SwapFailure(swap, swapped.Count < steps.Count ? steps[swapped.Count] : null);
            var undone = await UndoAsync(swapped, reload: false).ConfigureAwait(false);
            await ReEnableAsync(retiring).ConfigureAwait(false);
            await CleanAsync(uploads).ConfigureAwait(false);
            return swapped.Count == 0
                ? Aborted(why, inspections)
                : new HotplugReport(HotplugState.RolledBack, why + (undone ? string.Empty : UndoFailedNote), Items(swapped, inspections), inspections);
        }

        Set(HotplugState.ApplyingAndReloading, progress);
        var (failure, warning, reloadSent) = await ApplyAsync(swapped, clock).ConfigureAwait(false);
        if (failure is not null)
        {
            var undone = await UndoAsync(swapped, reload: true).ConfigureAwait(false);
            await ReEnableAsync(retiring).ConfigureAwait(false);
            await CleanAsync(uploads).ConfigureAwait(false);
            return new HotplugReport(HotplugState.RolledBack, failure + (undone ? string.Empty : UndoFailedNote), Items(swapped, inspections), inspections, SwapToReload: reloadSent);
        }

        await CleanAsync(uploads).ConfigureAwait(false);
        try
        {
            await HotplugBackups.PruneAsync(files, workspace.BackupFolder, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or Renci.SshNet.Common.SshException)
        {
            // Old backups stay a little longer; the next run prunes them.
        }

        return new HotplugReport(HotplugState.Completed, warning, Items(swapped, inspections), inspections, SwapToReload: reloadSent);
    }

    private const string UndoFailedNote = " Putting the old files back also failed in part; check the datapacks folder and .nut-backup on the VPS.";

    private async Task UploadAndVerifyAsync(List<DatapackInspection> inspections, List<HotplugStep> steps, HotplugLock held, IProgress<HotplugProgress>? progress, CancellationToken cancellationToken)
    {
        var total = Math.Max(1, inspections.Sum(i => i.SizeBytes));
        long done = 0;
        foreach (var (inspection, step) in inspections.Zip(steps))
        {
            Set(HotplugState.UploadingStaging, progress, inspection.FileName);
            var before = done;
            var fileProgress = progress is null ? null : new SyncProgress<double>(f =>
                progress.Report(new HotplugProgress(HotplugState.UploadingStaging, inspection.FileName, (before + (f * inspection.SizeBytes)) / total)));
            try
            {
                await connection.Files.UploadFileAsync(inspection.LocalPath, step.Staged! + ".part", fileProgress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or Renci.SshNet.Common.SshException)
            {
                throw new HotplugUploadException($"Uploading {inspection.FileName} failed: {e.Message}");
            }

            done += inspection.SizeBytes;
            if (!await held.RefreshAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new HotplugUploadException("The hot-plug lock was taken over by someone else; nothing was changed.");
            }
        }

        foreach (var (inspection, step) in inspections.Zip(steps))
        {
            Set(HotplugState.VerifyingRemote, progress, inspection.FileName);
            var hash = await connection.Runner.RunAsync(HotplugSwapCommands.Sha256(connection.Root, step.Staged! + ".part"), cancellationToken: cancellationToken).ConfigureAwait(false);
            switch (hash.ExitStatus)
            {
                case 0 when string.Equals(hash.Output.Trim(), inspection.Sha256, StringComparison.OrdinalIgnoreCase):
                    break;
                case 0:
                    throw new HotplugUploadException($"{inspection.FileName} arrived damaged on the VPS (its SHA-256 differs); nothing was changed.");
                case HotplugSwapCommands.NoHashToolExit:
                    throw new HotplugUploadException("The VPS has none of sha256sum, shasum or openssl, so the upload can't be checked.");
                default:
                    throw new HotplugUploadException($"Checking {inspection.FileName} on the VPS failed ({hash.ExitStatus}): {hash.Error.Trim()}");
            }

            var promote = await connection.Runner.RunAsync(HotplugSwapCommands.Promote(connection.Root, step.Staged! + ".part", step.Staged!), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (promote.ExitStatus != 0)
            {
                throw new HotplugUploadException($"Preparing {inspection.FileName} on the VPS failed ({promote.ExitStatus}): {promote.Error.Trim()}");
            }
        }
    }

    /// <summary>/reload, then the enabled list; packs it didn't enable by itself get /datapack enable. Returns why to roll back, if anything.</summary>
    private async Task<(string? Failure, string? Warning, TimeSpan? ReloadSent)> ApplyAsync(List<(HotplugStep Step, HotplugSwapResult Done)> swapped, Stopwatch clock)
    {
        var console = connection.Console;
        var put = swapped.Where(s => s.Done != HotplugSwapResult.Retired).ToList();
        if (put.Count == 0)
        {
            // Only removals: /datapack disable already reloaded.
            return (null, null, null);
        }

        using var watch = _hooks.Sentinel?.Begin(console);
        await console.SendCommandAsync("reload", CancellationToken.None).ConfigureAwait(false);
        var reloadSent = clock.Elapsed;

        var enabled = await ConsoleQuery.EnabledPacksAsync(console, ConsoleTimeout, CancellationToken.None).ConfigureAwait(false);
        string? warning = null;
        if (enabled is null)
        {
            warning = "The server didn't answer \"datapack list\" in time, so WaNut couldn't confirm the packs are enabled.";
        }
        else
        {
            // /reload enables new packs by itself unless they were disabled before; those need an explicit enable. An
            // updated pack keeps whatever state the administrator gave it.
            var off = put.Where(s => s.Done == HotplugSwapResult.Added && !enabled.Contains(HotplugNames.PackId(s.Step.FileName))).ToList();
            if (off.Count > 0)
            {
                foreach (var step in off)
                {
                    await console.SendCommandAsync("datapack enable " + HotplugNames.Quoted(step.Step.FileName), CancellationToken.None).ConfigureAwait(false);
                }

                var after = await ConsoleQuery.EnabledPacksAsync(console, ConsoleTimeout, CancellationToken.None).ConfigureAwait(false);
                var still = after is null ? [] : off.Where(s => !after.Contains(HotplugNames.PackId(s.Step.FileName))).ToList();
                if (still.Count > 0)
                {
                    return ($"The server didn't enable {string.Join(", ", still.Select(s => s.Step.FileName))}.", null, reloadSent);
                }

                warning = after is null ? "The server didn't answer \"datapack list\" in time after enabling the new packs." : null;
            }
        }

        if (watch is not null && await watch.FailureAsync(CancellationToken.None).ConfigureAwait(false) is { } failed)
        {
            return (failed, null, reloadSent);
        }

        return (null, warning, reloadSent);
    }

    /// <summary>Puts the old files back, newest step first, and reloads when the server had already loaded the new ones.</summary>
    private async Task<bool> UndoAsync(List<(HotplugStep Step, HotplugSwapResult Done)> swapped, bool reload)
    {
        if (swapped.Count == 0)
        {
            return true;
        }

        var undo = await connection.Runner.RunAsync(HotplugSwapCommands.Undo(connection.Root, swapped), cancellationToken: CancellationToken.None).ConfigureAwait(false);
        if (reload && swapped.Any(s => s.Done != HotplugSwapResult.Retired))
        {
            await connection.Console.SendCommandAsync("reload", CancellationToken.None).ConfigureAwait(false);
        }

        return undo.ExitStatus == 0;
    }

    private async Task ReEnableAsync(IEnumerable<HotplugStep> retiring)
    {
        foreach (var step in retiring)
        {
            await connection.Console.SendCommandAsync("datapack enable " + HotplugNames.Quoted(step.FileName), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task CleanAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            await connection.Runner.RunAsync(HotplugSwapCommands.Clean(connection.Root, paths), cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or Renci.SshNet.Common.SshException or ObjectDisposedException)
        {
            // Leftovers in the staging folder are harmless: never synced, overwritten by the next upload.
        }
    }

    private async Task AuditAsync(HotplugWorkspace workspace, HotplugReport report)
    {
        try
        {
            var line = HotplugAuditEntry.From(report, _time.GetUtcNow()).ToLine();
            await connection.Runner.RunAsync(HotplugSwapCommands.Append(connection.Root, workspace.AuditFile), line, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or Renci.SshNet.Common.SshException or ObjectDisposedException)
        {
            // The hot-plug itself is done; a missing audit line must not turn it into a failure.
        }
    }

    private static async Task<HashSet<string>> ListPacksAsync(IServerFileSystem files, string datapacks, CancellationToken cancellationToken)
    {
        try
        {
            var listed = await files.ListDirectoryAsync(datapacks, cancellationToken: cancellationToken).ConfigureAwait(false);
            return listed.Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static List<(HotplugStep Step, HotplugSwapResult Done)> ParseSwap(string output, List<HotplugStep> steps)
    {
        var done = new List<(HotplugStep, HotplugSwapResult)>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < lines.Length && i < steps.Count; i++)
        {
            HotplugSwapResult? result = lines[i] switch
            {
                "added" => HotplugSwapResult.Added,
                "replaced" => HotplugSwapResult.Replaced,
                "retired" => HotplugSwapResult.Retired,
                _ => null,
            };
            if (result is null)
            {
                break;
            }

            done.Add((steps[i], result.Value));
        }

        return done;
    }

    private static string SwapFailure(Remote.Files.RemoteCommandResult swap, HotplugStep? failed)
    {
        var name = failed?.FileName ?? "a pack";
        return swap.ExitStatus switch
        {
            HotplugSwapCommands.NotAFileExit => $"{name} in the datapacks folder is a folder or a link, not a zip; it was left alone.",
            HotplugSwapCommands.BackupTakenExit => $"A backup of {name} with the same time stamp already exists; try again in a second.",
            HotplugSwapCommands.MissingExit => $"The checked upload of {name} disappeared from the staging folder.",
            Remote.Files.RemoteFileCommands.NoFolderExit or Remote.Files.RemoteFileCommands.MovedExit => "The server folder on the VPS moved or disappeared.",
            _ => $"Moving {name} into place failed ({swap.ExitStatus}): {swap.Error.Trim()}",
        };
    }

    private static List<HotplugItem> Items(List<(HotplugStep Step, HotplugSwapResult Done)> swapped, List<DatapackInspection> inspections) =>
        swapped.Select(s => new HotplugItem(s.Step.FileName, s.Done, inspections.FirstOrDefault(i => i.FileName == s.Step.FileName)?.Sha256)).ToList();

    private static string? CheckNames(HotplugRequest request)
    {
        if (request.Add.Count == 0 && request.Remove.Count == 0)
        {
            return "Nothing to hot-plug.";
        }

        var adding = request.Add.Select(Path.GetFileName).OfType<string>().ToList();
        if (adding.Concat(request.Remove).GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            return $"{twice.Key} is in the batch twice.";
        }

        return request.Remove.Select(HotplugNames.Problem).FirstOrDefault(p => p is not null);
    }

    private static string BlockedReason(DatapackInspection i) =>
        i.Problem is { } p ? $"{i.FileName}: {p}"
        : $"{i.FileName} adds {string.Join(" and ", i.RiskyFolders)}, which only load when the server starts. Stop the server and add it in the Packs tab instead.";

    private static HotplugReport Aborted(string message, IReadOnlyList<DatapackInspection> inspections) =>
        new(HotplugState.Aborted, message, [], inspections);

    private void Set(HotplugState state, IProgress<HotplugProgress>? progress, string? detail = null)
    {
        State = state;
        progress?.Report(new HotplugProgress(state, detail));
    }

    private sealed class HotplugUploadException(string message) : Exception(message);
}
