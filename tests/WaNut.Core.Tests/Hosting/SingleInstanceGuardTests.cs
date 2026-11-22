using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public class SingleInstanceGuardTests
{
    private static string NewName() => "WaNutTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryAcquire_FirstStart_ReturnsGuard()
    {
        using var guard = SingleInstanceGuard.TryAcquire(NewName());

        Assert.NotNull(guard);
    }

    [Fact]
    public async Task TryAcquire_SecondStart_ReturnsNullAndAsksFirstToComeForward()
    {
        var name = NewName();
        using var first = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(first);
        var asked = new TaskCompletionSource();
        first.ActivationRequested += (_, _) => asked.TrySetResult();
        first.StartListening();

        // A mutex is owned per thread, so the second start comes from another thread, like another process would.
        var second = await Task.Run(() => SingleInstanceGuard.TryAcquire(name));

        Assert.Null(second);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryAcquire_AfterFirstIsDisposed_ReturnsGuardAgain()
    {
        var name = NewName();
        SingleInstanceGuard.TryAcquire(name)!.Dispose();

        using var again = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(again);
    }
}
