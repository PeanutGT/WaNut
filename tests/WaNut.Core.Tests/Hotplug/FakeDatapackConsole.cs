using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hotplug;

/// <summary>
/// A console that answers datapack commands the way Minecraft does, from the real datapacks folder: /reload enables
/// new packs unless they were disabled before, and "datapack list enabled" lists what is on.
/// </summary>
internal sealed class FakeDatapackConsole(string datapacksFolder) : IServerConsole
{
    public event Action<string>? OutputReceived;

    public List<string> Commands { get; } = [];

    public HashSet<string> Enabled { get; } = [];

    public HashSet<string> Disabled { get; } = [];

    /// <summary>Packs the server refuses to enable, like a broken one.</summary>
    public HashSet<string> Refuse { get; } = [];

    /// <summary>When false, "datapack list" gets no answer.</summary>
    public bool Answers { get; set; } = true;

    public Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        if (command == "reload")
        {
            var files = Directory.Exists(datapacksFolder) ? Directory.GetFiles(datapacksFolder).Select(Path.GetFileName).ToHashSet() : [];
            Enabled.RemoveWhere(n => !files.Contains(n));
            foreach (var name in files.OfType<string>().Where(n => !Disabled.Contains(n) && !Refuse.Contains(n)))
            {
                Enabled.Add(name);
            }

            Say("Reloading!");
        }
        else if (command == "datapack list enabled" && Answers)
        {
            Say(Enabled.Count == 0
                ? "There are no data packs enabled"
                : $"There are {Enabled.Count + 1} data pack(s) enabled: [vanilla (built-in)], " + string.Join(", ", Enabled.Order().Select(n => $"[file/{n} (world)]")));
        }
        else if (Name(command, "datapack enable ") is { } on && !Refuse.Contains(on) && File.Exists(Path.Combine(datapacksFolder, on)))
        {
            Enabled.Add(on);
            Disabled.Remove(on);
        }
        else if (Name(command, "datapack disable ") is { } off)
        {
            Enabled.Remove(off);
            Disabled.Add(off);
        }

        return Task.CompletedTask;
    }

    private void Say(string text) => OutputReceived?.Invoke($"[12:00:00] [Server thread/INFO]: {text}");

    private static string? Name(string command, string prefix) =>
        command.StartsWith(prefix, StringComparison.Ordinal) ? command[prefix.Length..].Trim('"')["file/".Length..] : null;
}
