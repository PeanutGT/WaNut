using System.IO;
using WaNut.App.ViewModels;
using WaNut.Core.Backups;

namespace WaNut.App.Services;

/// <summary>
/// Copies every new backup to the folder chosen in the Backups tab (often OneDrive), so a world survives even if
/// the PC's disk dies. A failed copy is only reported; it never stops a server from starting.
/// </summary>
public sealed class BackupCopyService
{
    private readonly BackupStore _store;
    private readonly AppSettingsViewModel _app;
    private readonly SynchronizationContext _ui;

    public BackupCopyService(BackupStore store, AppSettingsViewModel app)
    {
        _store = store;
        _app = app;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        store.Created += OnCreated;
    }

    /// <summary>Raised on the UI thread with the server folder and the reason a copy failed.</summary>
    public event Action<string, string>? CopyFailed;

    /// <summary>The OneDrive folder's "WaNut backups", or null when OneDrive isn't set up on this PC.</summary>
    public static string? SuggestedFolder()
    {
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } dir && Directory.Exists(dir))
            {
                return Path.Combine(dir, "WaNut backups");
            }
        }

        return null;
    }

    private void OnCreated(string instanceDir, BackupInfo backup)
    {
        if (_app.BackupCopyDir.Trim() is not { Length: > 0 } root)
        {
            return;
        }

        // One folder per server, named after its folder, so two servers' backups never mix.
        var dir = Path.Combine(root, Path.GetFileName(instanceDir));
        var rules = _app.BackupRules;
        _ = Task.Run(async () =>
        {
            try
            {
                await _store.CopyToFolderAsync(backup, dir, rules);
            }
            catch (Exception e)
            {
                // Any failure (a full drive, a folder that went away, OneDrive locking the file) is only reported.
                _ui.Post(_ => CopyFailed?.Invoke(instanceDir, string.Format(Resources.Text.Get("BackupCopy_Failed"), dir, e.Message)), null);
            }
        });
    }
}
