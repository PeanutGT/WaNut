using WaNut.Core.Downloads;

namespace WaNut.Core.Tests.Downloads;

public sealed class ProgressEstimatorTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Add_BeforeSettling_GivesNoGuess()
    {
        var estimator = new ProgressEstimator();

        estimator.Add(S(0), 0);
        var estimate = estimator.Add(S(1), 0.1, 1_000_000);

        Assert.Null(estimate.Remaining);
        Assert.Null(estimate.BytesPerSecond);
    }

    [Fact]
    public void Add_SteadyPace_GuessesTimeLeftAndSpeed()
    {
        var estimator = new ProgressEstimator();

        estimator.Add(S(0), 0, 0);
        estimator.Add(S(2), 0.1, 2_000_000);
        var estimate = estimator.Add(S(4), 0.2, 4_000_000);

        Assert.Equal(16, estimate.Remaining!.Value.TotalSeconds, 3);
        Assert.Equal(1_000_000, estimate.BytesPerSecond!.Value, 3);
    }

    [Fact]
    public void Add_PaceChanges_FollowsRecentSamplesOnly()
    {
        var estimator = new ProgressEstimator();

        // Slow for 20 seconds, then ten times faster.
        estimator.Add(S(0), 0);
        estimator.Add(S(20), 0.1);
        for (var t = 21; t <= 30; t++)
        {
            estimator.Add(S(t), 0.1 + ((t - 20) * 0.05));
        }

        var estimate = estimator.Add(S(31), 0.65);

        // 0.05 per second over the window, 0.35 left.
        Assert.Equal(7, estimate.Remaining!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void Add_NoProgress_GivesNoTimeLeft()
    {
        var estimator = new ProgressEstimator();

        estimator.Add(S(0), 0.5);
        var estimate = estimator.Add(S(3), 0.5);

        Assert.Null(estimate.Remaining);
        Assert.Null(estimate.BytesPerSecond);
    }

    [Fact]
    public void Reset_StartsOver()
    {
        var estimator = new ProgressEstimator();
        estimator.Add(S(0), 0);
        estimator.Add(S(5), 0.5);

        estimator.Reset();
        var estimate = estimator.Add(S(6), 0.1);

        Assert.Null(estimate.Remaining);
    }
}
