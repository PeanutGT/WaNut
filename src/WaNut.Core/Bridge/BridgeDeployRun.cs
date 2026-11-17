namespace WaNut.Core.Bridge;

/// <summary>
/// One deployment in progress: reports each step and remembers how to undo what was changed, so a failure in any
/// step leaves the host as it was before (the plan's rollback rule).
/// </summary>
public sealed class BridgeDeployRun
{
    private readonly IProgress<BridgeDeployStep>? _progress;
    private readonly Stack<Func<Task>> _undo = new();

    private BridgeDeployRun(IProgress<BridgeDeployStep>? progress) => _progress = progress;

    public BridgeDeployStep Step { get; private set; } = BridgeDeployStep.Validate;

    /// <summary>Runs <paramref name="body"/>; on any failure undoes its changes, newest first, and throws a <see cref="BridgeDeployException"/>.</summary>
    public static async Task<T> RunAsync<T>(IProgress<BridgeDeployStep>? progress, Func<BridgeDeployRun, Task<T>> body)
    {
        var run = new BridgeDeployRun(progress);
        try
        {
            return await body(run).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await run.UndoAsync().ConfigureAwait(false);
            if (ex is BridgeDeployException or OperationCanceledException)
            {
                throw;
            }

            throw new BridgeDeployException(run.Step, DefaultError(run.Step), ex.Message, ex);
        }
    }

    public void Enter(BridgeDeployStep step)
    {
        Step = step;
        _progress?.Report(step);
    }

    /// <summary>Registers how to take back a change that was just made. Undo steps never get the cancelled token.</summary>
    public void OnFailure(Func<Task> undo) => _undo.Push(undo);

    public void OnFailure(Action undo) => _undo.Push(() =>
    {
        undo();
        return Task.CompletedTask;
    });

    public BridgeDeployException Fail(BridgeDeployError error, string? detail = null, Exception? inner = null) => new(Step, error, detail, inner);

    public static BridgeDeployError DefaultError(BridgeDeployStep step) => step switch
    {
        BridgeDeployStep.Validate => BridgeDeployError.InvalidRequest,
        BridgeDeployStep.Probe => BridgeDeployError.VpsUnreachable,
        BridgeDeployStep.PatchServerProperties or BridgeDeployStep.CopyFiles => BridgeDeployError.CopyFailed,
        BridgeDeployStep.Register => BridgeDeployError.RegisterFailed,
        _ => BridgeDeployError.NoAnswer,
    };

    private async Task UndoAsync()
    {
        while (_undo.TryPop(out var undo))
        {
            try
            {
                await undo().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort: one undo that fails must not keep the others from running.
            }
        }
    }
}
