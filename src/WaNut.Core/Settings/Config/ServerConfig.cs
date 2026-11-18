using WaNut.Core.Instances;
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
    private readonly Dictionary<string, string> _rules;
    private readonly Dictionary<string, string> _savedRules;
    private readonly GameFeatures _features;

    private ServerConfig(Dictionary<string, string> store, Dictionary<string, string> rules, GameFeatures features, List<ConfigItem> items)
    {
        _store = store;
        _saved = new Dictionary<string, string>(store, StringComparer.Ordinal);
        _rules = rules;
        _savedRules = new Dictionary<string, string>(rules, StringComparer.Ordinal);
        _features = features;
        Items = items;
    }

    /// <summary>Catalog items in catalog order, then unknown keys in file order.</summary>
    public IReadOnlyList<ConfigItem> Items { get; }

    /// <summary>The items of a section. Game rule items (they are not file keys) come with their section, in catalog order.</summary>
    public IEnumerable<ConfigItem> InSection(ConfigSection section) => Items.Where(i => i.Meta.Section == section);

    /// <summary>The items that are keys of server.properties: the full property view lists these.</summary>
    public IEnumerable<ConfigItem> FileItems => Items.Where(i => !i.Meta.IsGameRuleItem);

    public ConfigItem? Find(string key) => Items.FirstOrDefault(i => i.Meta.Key == key);

    public static ServerConfig Load(PropertiesFile file, string minecraftVersion) =>
        Load(file, minecraftVersion, new Dictionary<string, bool>());

    /// <summary>
    /// <paramref name="gameRules"/> are the rules the user set (<see cref="InstanceMetadata.GameRules"/>). Keep inventory, and
    /// on 1.21.9+ also PvP, command blocks and monster spawning, become game rule items in place of the file keys.
    /// </summary>
    public static ServerConfig Load(PropertiesFile file, string minecraftVersion, IReadOnlyDictionary<string, bool> gameRules)
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in file.Keys)
        {
            store[key] = file.Get(key) ?? string.Empty;
        }

        var features = GameFeatures.For(minecraftVersion);
        var version = GameFeatures.ParseVersion(minecraftVersion);
        var rules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in gameRules)
        {
            rules[name] = value ? "true" : "false";
        }

        var items = new List<ConfigItem>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var meta in ServerConfigCatalog.All.Where(m => m.Since is null || version >= m.Since))
        {
            if (meta.GameRule is { } rule && features.IsGameRule(rule))
            {
                var ruleMeta = meta with
                {
                    Key = GameSettingsStore.RuleKey(rule),
                    DefaultValue = features.DefaultValue(rule) ? "true" : "false",
                    TextKey = meta.Key,
                    IsGameRuleItem = true,
                };
                items.Add(new ConfigItem(ruleMeta, rules));
                continue;
            }

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

        return new ServerConfig(store, rules, features, items);
    }

    /// <summary>
    /// Writes the changed values into <paramref name="file"/> (freshly loaded by the caller, so edits made to the
    /// file meanwhile are kept). A key the file lacked is added only when its value is not the default.
    /// </summary>
    public bool ApplyTo(PropertiesFile file)
    {
        var changed = false;
        foreach (var item in FileItems)
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

    /// <summary>
    /// Records the changed game rules in <paramref name="metadata"/> (the caller saves it) and returns the
    /// "gamerule" commands that bring a running server in line. Only rules that differ from the last save are touched.
    /// </summary>
    public InstanceMetadata ApplyRulesTo(InstanceMetadata metadata, out IReadOnlyList<string> commands)
    {
        var rules = new Dictionary<string, bool>(metadata.GameRules);
        var sent = new List<string>();
        foreach (var item in Items.Where(i => i.Meta.IsGameRuleItem))
        {
            var key = item.Meta.Key;
            var before = _savedRules.TryGetValue(key, out var saved) ? saved : item.Meta.DefaultValue;
            if (string.Equals(before, item.RawValue, StringComparison.Ordinal))
            {
                continue;
            }

            rules[key] = item.BoolValue;
            _savedRules[key] = item.RawValue;
            var rule = item.Meta.GameRule!.Value;
            if (_features.RuleName(rule) is { } name)
            {
                sent.Add($"gamerule {name} {item.RawValue}");
            }
        }

        commands = sent;
        return sent.Count == 0 && rules.Count == metadata.GameRules.Count && rules.All(r => metadata.GameRules.TryGetValue(r.Key, out var v) && v == r.Value)
            ? metadata
            : metadata with { GameRules = rules };
    }
}
