using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.Core.Downloads;

namespace WaNut.App.ViewModels;

/// <summary>"About 1 min 20 s left 繚 12.3 MB/s" under a progress bar, from the progress reported so far.</summary>
public sealed partial class ProgressEta : ObservableObject
{
    private readonly ProgressEstimator _estimator = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Null until there is enough to guess from.</summary>
    [ObservableProperty]
    private string? _text;

    /// <param name="fraction">0 to 1, or null when the job can't say how far it got (nothing is guessed then).</param>
    /// <param name="bytes">Bytes downloaded so far, to show the speed.</param>
    public void Report(double? fraction, long? bytes = null)
    {
        if (fraction is not { } done)
        {
            Reset();
            return;
        }

        Text = Format(_estimator.Add(_clock.Elapsed, done, bytes));
    }

    public void Reset()
    {
        _estimator.Reset();
        Text = null;
    }

    private static string? Format(ProgressEstimate estimate)
    {
        var parts = new List<string>(2);
        if (estimate.Remaining is { } left)
        {
            // Whole seconds under a minute; past that, steps of 10 seconds so the text doesn't flicker.
            var seconds = (int)Math.Ceiling(left.TotalSeconds);
            parts.Add(seconds < 60
                ? string.Format(Resources.Text.Get("Eta_Seconds"), Math.Max(1, seconds))
                : string.Format(Resources.Text.Get("Eta_Minutes"), seconds / 60, seconds % 60 / 10 * 10));
        }

        if (estimate.BytesPerSecond is { } speed)
        {
            parts.Add(string.Format(Resources.Text.Get("Eta_Speed"), speed / (1024 * 1024)));
        }

        return parts.Count == 0 ? null : string.Join(" 繚 ", parts);
    }
}
