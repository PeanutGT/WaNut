using System.Windows;
using System.Windows.Media.Animation;

namespace WaNut.App.Services;

/// <summary>
/// Follows the Windows "Animation effects" setting: when it is off, the shared durations in Themes/Motion.xaml
/// become zero, so every transition turns into a direct switch.
/// </summary>
public static class MotionSettings
{
    private static readonly string[] DurationKeys = ["MotionFeedback", "MotionTransition", "MotionBounce"];

    /// <summary>False when Windows has animation effects off; code-driven transitions then switch directly.</summary>
    public static bool IsOn => Transition.HasTimeSpan && Transition.TimeSpan > TimeSpan.Zero;

    /// <summary>The shared time for content that changes place, for animations started from code.</summary>
    public static Duration Transition => (Duration)Application.Current.FindResource("MotionTransition");

    /// <summary>The shared crisp curve, for animations started from code.</summary>
    public static IEasingFunction Ease => (IEasingFunction)Application.Current.FindResource("MotionEase");

    /// <summary>Call before the first window opens: styles read the durations once, when they are first used.</summary>
    public static void Apply(Application app, bool animationsEnabled)
    {
        if (animationsEnabled)
        {
            return;
        }

        foreach (var dictionary in app.Resources.MergedDictionaries)
        {
            foreach (var key in DurationKeys)
            {
                if (dictionary.Contains(key))
                {
                    dictionary[key] = new Duration(TimeSpan.Zero);
                }
            }
        }
    }
}
