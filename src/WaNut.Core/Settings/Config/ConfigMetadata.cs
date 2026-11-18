using System.Text;

namespace WaNut.Core.Settings.Config;

/// <summary>Describes one server.properties key: where it is shown, how it is edited and which values are valid.</summary>
public sealed record ConfigMetadata(
    string Key,
    ConfigSection Section,
    ConfigControlType ControlType,
    string DefaultValue,
    IReadOnlyList<ConfigOption>? Options = null,
    double Min = 0,
    double Max = 100,
    string? WarningBannerKey = null,
    string? WarningTriggerValue = null)
{
    /// <summary>Resource key of the display name, such as <c>ServerConfig_AllowFlight_Title</c>.</summary>
    public string TitleKey => KeyFor(TextKey ?? Key, "Title");

    public string DescriptionKey => KeyFor(TextKey ?? Key, "Description");

    /// <summary>The key the texts are named after when it differs from <see cref="Key"/> (a game rule item keeps the text of the property it replaces).</summary>
    public string? TextKey { get; init; }

    /// <summary>Resource key of a gentle inline hint shown while a number is above <see cref="HintAbove"/>; null for none.</summary>
    public string? HintKey { get; init; }

    /// <summary>The number above which <see cref="HintKey"/> shows.</summary>
    public double HintAbove { get; init; }

    /// <summary>True for an item that is a game rule (saved in instance.json, sent as a "gamerule" command), not a server.properties key.</summary>
    public bool IsGameRuleItem { get; init; }

    /// <summary>True when a running server only reads the value at start-up.</summary>
    public bool RequiresRestart { get; init; }

    /// <summary>First Minecraft version that has the key; null when every supported version does.</summary>
    public Version? Since { get; init; }

    /// <summary>Set when the key turns into a game rule on newer versions (see <see cref="GameFeatures"/>).</summary>
    public RuleSetting? GameRule { get; init; }

    /// <summary>For a key found in the file that the catalog does not know: shown as plain text, never dropped.</summary>
    public static ConfigMetadata ForUnknownKey(string key) => new(key, ConfigSection.RawOnly, ConfigControlType.Text, string.Empty);

    /// <summary>"allow-flight" → "ServerConfig_AllowFlight_Title".</summary>
    public static string KeyFor(string key, string part)
    {
        var builder = new StringBuilder("ServerConfig_");
        var upper = true;
        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return builder.Append('_').Append(part).ToString();
    }
}
