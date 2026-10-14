namespace WaNut.Core.Downloads;

/// <summary>An <see cref="IProgress{T}"/> that runs its handler immediately on the reporting thread.</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
