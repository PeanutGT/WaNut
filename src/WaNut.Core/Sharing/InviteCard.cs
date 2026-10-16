using System.Text;

namespace WaNut.Core.Sharing;

/// <summary>What goes on an invite card. <paramref name="Addresses"/> pairs a network's name with its "IP:port".</summary>
public sealed record InviteCardContent(
    string ServerName,
    string TypeAndVersion,
    IReadOnlyList<(string Network, string Endpoint)> Addresses,
    string? Note,
    IReadOnlyList<string> Mods);

/// <summary>The card's wording, from the UI's resource files. Each takes the values named in its comment.</summary>
/// <param name="Title">{0}: server name.</param>
/// <param name="Version">{0}: type and version, e.g. "Fabric 1.21.1".</param>
/// <param name="Addresses">A heading for the address list.</param>
/// <param name="NoAddress">Shown when no address is known.</param>
/// <param name="Note">{0}: the host's note, such as the VPN network name.</param>
/// <param name="Mods">A heading for the mod list.</param>
public sealed record InviteCardLabels(string Title, string Version, string Addresses, string NoAddress, string Note, string Mods);

/// <summary>A short text a host pastes into Discord or LINE so friends know how to join.</summary>
public static class InviteCard
{
    public static string Compose(InviteCardContent content, InviteCardLabels labels)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Format(labels.Title, content.ServerName));
        text.AppendLine(string.Format(labels.Version, content.TypeAndVersion));
        text.AppendLine();
        text.AppendLine(labels.Addresses);
        if (content.Addresses.Count == 0)
        {
            text.AppendLine("• " + labels.NoAddress);
        }

        foreach (var (network, endpoint) in content.Addresses)
        {
            text.AppendLine($"• {network}: {endpoint}");
        }

        if (!string.IsNullOrWhiteSpace(content.Note))
        {
            text.AppendLine();
            text.AppendLine(string.Format(labels.Note, content.Note.Trim()));
        }

        if (content.Mods.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(labels.Mods);
            foreach (var mod in content.Mods.Order(StringComparer.CurrentCultureIgnoreCase))
            {
                text.AppendLine("• " + mod);
            }
        }

        return text.ToString().TrimEnd();
    }
}
