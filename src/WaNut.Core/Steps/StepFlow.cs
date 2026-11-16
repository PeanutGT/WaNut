namespace WaNut.Core.Steps;

public enum StepState
{
    Waiting,
    Current,
    Done,
}

/// <summary>
/// The steps of one long job (create, start, stop, back up), in order, moved by real progress only. A job never goes
/// back a step, and steps it passed over (Java was already there) count as done.
/// </summary>
public sealed class StepFlow
{
    public StepFlow(string name, IReadOnlyList<string> steps)
    {
        if (steps.Count == 0)
        {
            throw new ArgumentException("A flow needs at least one step.", nameof(steps));
        }

        Name = name;
        Steps = steps;
    }

    /// <summary>Which job this is, e.g. <see cref="StepLists.Start"/>.</summary>
    public string Name { get; }

    public IReadOnlyList<string> Steps { get; }

    public int Current { get; private set; }

    /// <summary>The job is done: every step, the last one too, shows as done.</summary>
    public bool IsFinished { get; private set; }

    public string CurrentStep => Steps[Current];

    /// <summary>Moves on to <paramref name="step"/>. False when it is not in this flow, already behind, or the flow finished.</summary>
    public bool MoveTo(string step)
    {
        var index = IndexOf(step);
        if (IsFinished || index <= Current)
        {
            return false;
        }

        Current = index;
        return true;
    }

    public void Finish()
    {
        Current = Steps.Count - 1;
        IsFinished = true;
    }

    public bool Contains(string step) => IndexOf(step) >= 0;

    public StepState StateOf(int index) =>
        IsFinished || index < Current ? StepState.Done : index == Current ? StepState.Current : StepState.Waiting;

    private int IndexOf(string step)
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            if (Steps[i] == step)
            {
                return i;
            }
        }

        return -1;
    }
}
