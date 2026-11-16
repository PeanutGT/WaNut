using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Steps;

namespace WaNut.App.ViewModels;

/// <summary>
/// The loading strip of one long job (create, start, stop, back up): "02 / 05" with the current step's title and
/// a row of step circles joined by a line that fills as the job moves on. Only real progress moves it.
/// </summary>
public sealed partial class StepsViewModel : ObservableObject
{
    /// <summary>How long a finished strip stays on screen, all steps done, before it folds away.</summary>
    private static readonly TimeSpan FinishedShownFor = TimeSpan.FromSeconds(2);

    private StepFlow? _flow;

    // Bumped by every Begin and Hide, so a finished strip's delayed hide never hides the next job's strip.
    private int _generation;

    public ObservableCollection<StepItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private string _number = string.Empty;

    [ObservableProperty]
    private string _total = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _help = string.Empty;

    /// <summary>The job running now, e.g. <see cref="StepLists.Start"/>, or null when the strip is hidden.</summary>
    public string? Job => IsShown ? _flow?.Name : null;

    public bool IsFinished => _flow?.IsFinished == true;

    /// <summary>Shows a new job's steps, replacing any strip on screen.</summary>
    public void Begin(StepFlow flow)
    {
        _generation++;
        _flow = flow;
        Items.Clear();
        for (var i = 0; i < flow.Steps.Count; i++)
        {
            Items.Add(new StepItemViewModel(flow.Steps[i], isFirst: i == 0, isLast: i == flow.Steps.Count - 1));
        }

        Total = flow.Steps.Count.ToString("00");
        Refresh();
        IsShown = true;
        OnPropertyChanged(nameof(Job));
    }

    /// <summary>Moves the running <paramref name="job"/> on to <paramref name="step"/>; does nothing for another job or a step behind.</summary>
    public void MoveTo(string job, string step)
    {
        if (Job == job && _flow!.MoveTo(step))
        {
            Refresh();
        }
    }

    /// <summary>Every step done; the strip folds away a moment later unless another job begins.</summary>
    public void Finish(string job)
    {
        if (Job != job || _flow!.IsFinished)
        {
            return;
        }

        _flow.Finish();
        Refresh();
        _ = HideLaterAsync(_generation);
    }

    private async Task HideLaterAsync(int generation)
    {
        await Task.Delay(FinishedShownFor);
        if (generation == _generation)
        {
            Hide();
        }
    }

    /// <summary>Hides the strip of <paramref name="job"/>, e.g. after it failed; a strip of another job stays.</summary>
    public void Hide(string job)
    {
        if (Job == job)
        {
            Hide();
        }
    }

    private void Hide()
    {
        _generation++;
        IsShown = false;
        OnPropertyChanged(nameof(Job));
    }

    private void Refresh()
    {
        var flow = _flow!;
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].State = flow.StateOf(i);
        }

        Number = (flow.Current + 1).ToString("00");
        Title = Text.Get($"Steps_{flow.CurrentStep}_Title");
        Help = Text.Get($"Steps_{flow.CurrentStep}_Help");
        OnPropertyChanged(nameof(IsFinished));
    }
}

/// <summary>One circle of the loading strip with its icon and label.</summary>
public sealed partial class StepItemViewModel(string step, bool isFirst, bool isLast) : ObservableObject
{
    // Segoe Fluent Icons / Segoe MDL2 Assets glyphs; both fonts use these code points.
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["Version"] = "",
        ["Java"] = "",
        ["ServerFiles"] = "",
        ["Loader"] = "",
        ["Settings"] = "",
        ["CopyWorld"] = "",
        ["WorldMods"] = "",
        ["Modpack"] = "",
        ["Ready"] = "",
        ["Backup"] = "",
        ["Connect"] = "",
        ["Upload"] = "",
        ["Launch"] = "",
        ["LoadWorld"] = "",
        ["Online"] = "",
        ["Players"] = "",
        ["SaveWorld"] = "",
        ["Close"] = "",
        ["BringBack"] = "",
        ["Stopped"] = "",
        ["Flush"] = "",
        ["Pack"] = "",
        ["Saved"] = "",
    };

    public string Label { get; } = Text.Get($"Steps_{step}");

    public string Glyph { get; } = Glyphs.GetValueOrDefault(step, "");

    /// <summary>The first circle has no line on its left, the last none on its right.</summary>
    public bool IsFirst { get; } = isFirst;

    public bool IsLast { get; } = isLast;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent), nameof(IsDone), nameof(IsReached))]
    private StepState _state;

    public bool IsCurrent => State == StepState.Current;

    public bool IsDone => State == StepState.Done;

    /// <summary>The line up to this circle is filled: the job got here.</summary>
    public bool IsReached => State != StepState.Waiting;
}
