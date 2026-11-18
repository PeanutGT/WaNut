using WaNut.Core.Properties;

namespace WaNut.Core.Settings.Config;

/// <summary>
/// The editable view of a server.properties file: one <see cref="ConfigItem"/> per catalog key, then one per key
/// the catalog does not know (modpack settings, new vanilla keys). All items share one value store. Saving writes
/// only the keys that changed, through <see cref="PropertiesFile"/>, so comments, order and unknown lines stay.
/// </summary>
public sealed class ServerConfig
{
    private readonly Dictionary<string, string> _store;
    private readonly Dictionary<string, string> _saved;

    private ServerConfig(Dictionary<string, string> store, List<ConfigItem> items)
    {
        _store = store;
        _saved = new Dictionary<string, string>(store, StringComparer.Ordinal);
        Items = items;
    }

    /// <summary>Catalog items in catalog order, then unknown keys in file order.</summary>
    public IReadOnlyList<ConfigItem> Items { get; }

    public IEnumerable<ConfigItem> InSection(ConfigSection section) => Items.Where(i => i.Meta.Section == section);

    public ConfigItem? Find(string key) => Items.FirstOrDefault(i => i.Meta.Key == key);

    public static ServerConfig Load(PropertiesFile file, string minecraftVersion)
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in file.Keys)
        {
            store[key] = file.Get(key) ?? string.Empty;
        }

        var items = new List<ConfigItem>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var meta in ServerConfigCatalog.For(GameFeatures.For(minecraftVersion), minecraftVersion))
        {
            known.Add(meta.Key);
            items.Add(new ConfigItem(meta, store));
        }

        // Keys the catalog hides on this version (such as pvp after 1.21.9) are still in the file; keep them too.
        foreach (var key in file.Keys.Where(k => !known.Contains(k)))
        {
            var meta = ServerConfigCatalog.All.FirstOrDefault(m => m.Key == key) is { } hidden
                ? hidden with { Section = ConfigSection.RawOnly }
                : ConfigMetadata.ForUnknownKey(key);
            items.Add(new ConfigItem(meta, store));
        }

        return new ServerConfig(store, items);
    }

    /// <summary>
    /// Writes the changed values into <paramref name="file"/> (freshly loaded by the caller, so edits made to the
    /// file meanwhile are kept). A key the file lacked is added only when its value is not the default.
    /// </summary>
    public bool ApplyTo(PropertiesFile file)
    {
        var changed = false;
        foreach (var item in Items)
        {
            var key = item.Meta.Key;
            if (!_store.TryGetValue(key, out var value))
            {
                continue;
            }

            var hadKey = _saved.TryGetValue(key, out var before);
            if (hadKey ? string.Equals(before, value, StringComparison.Ordinal)
                       : string.Equals(value, item.Meta.DefaultValue, StringComparison.Ordinal))
            {
                continue;
            }

            file.Set(key, value);
            _saved[key] = value;
            changed = true;
        }

        return changed;
    }
}
