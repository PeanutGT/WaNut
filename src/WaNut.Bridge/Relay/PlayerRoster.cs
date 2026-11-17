namespace WaNut.Bridge.Relay;

/// <summary>
/// Who is in the game, from join and leave lines and the <c>list</c> command. Used to tell a real death line from a
/// server message that happens to start with a name and "was". Until the first <c>list</c> answer it is not known
/// who was already online, and every name is let through.
/// </summary>
public sealed class PlayerRoster
{
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _known;

    public void Add(string name)
    {
        lock (_gate)
        {
            _names.Add(name);
        }
    }

    public void Remove(string name)
    {
        lock (_gate)
        {
            _names.Remove(name);
        }
    }

    /// <summary>The server (re)started or stopped: nobody is online.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _names.Clear();
            _known = true;
        }
    }

    public void Replace(IEnumerable<string> names)
    {
        lock (_gate)
        {
            _names.Clear();
            _names.UnionWith(names);
            _known = true;
        }
    }

    public bool MayBeOnline(string name)
    {
        lock (_gate)
        {
            return !_known || _names.Contains(name);
        }
    }
}
