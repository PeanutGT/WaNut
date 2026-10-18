using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Onboarding;

namespace WaNut.App.ViewModels;

/// <summary>The peanut-led tour shown over the main window. Progress is saved when a part ends or is skipped.</summary>
public sealed partial class GuideViewModel(AppSettingsViewModel settings) : ObservableObject
{
    private IReadOnlyList<GuideStop> _stops = [];
    private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(Title), nameof(Body), nameof(StepText), nameof(NextText), nameof(CanGoBack))]
    private GuideStop? _current;

    public bool IsOpen => Current is not null;

    public string? Title => Current is { } stop ? Text.Get($"Guide_{stop.Id}Title") : null;

    public string? Body => Current is { } stop ? Text.Get($"Guide_{stop.Id}Text") : null;

    public string StepText => string.Format(Text.Get("Guide_Step"), _index + 1, _stops.Count);

    public string NextText => Text.Get(_index == _stops.Count - 1 ? "Guide_Finish" : "Guide_Next");

    public bool CanGoBack => _index > 0;

    /// <summary>The stops the saved progress still calls for, or none.</summary>
    public IReadOnlyList<GuideStop> Pending(bool hasServer) =>
        GuideTour.Plan(settings.GuideIntroDone, settings.GuideTabsDone, hasServer);

    /// <summary>The whole tour, for "Show the guide again".</summary>
    public static IReadOnlyList<GuideStop> Everything(bool hasServer) =>
        GuideTour.Plan(introDone: false, tabsDone: false, hasServer);

    public void Start(IReadOnlyList<GuideStop> stops)
    {
        if (stops.Count == 0 || IsOpen)
        {
            return;
        }

        _stops = stops;
        Show(0);
    }

    [RelayCommand]
    private void Next()
    {
        if (_index < _stops.Count - 1)
        {
            Show(_index + 1);
            return;
        }

        // A tour that showed the tabs has done both parts; one without them leaves the tabs for the first server.
        Close(tabsDone: settings.GuideTabsDone || GuideTour.NeedsServer(_stops));
    }

    [RelayCommand]
    private void Back()
    {
        if (_index > 0)
        {
            Show(_index - 1);
        }
    }

    /// <summary>Skipping ends the tour for good, tabs included; App settings can still show it again.</summary>
    [RelayCommand]
    private void Skip() => Close(tabsDone: true);

    private void Show(int index)
    {
        _index = index;
        Current = _stops[index];
    }

    private void Close(bool tabsDone)
    {
        _stops = [];
        _index = 0;
        Current = null;
        settings.SaveGuideProgress(introDone: true, tabsDone);
    }
}
