using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WaNut.Core.Tunnels;

/// <summary>
/// The parts of playit.gg's API that WaNut needs: approving this PC once in the browser (a "claim"), then creating and
/// listing the agent's own Minecraft tunnels. Every call is a JSON POST answered with {"status", "data"}.
/// </summary>
public sealed class PlayitApi(HttpClient http, Uri? apiBase = null)
{
    public static readonly Uri DefaultApiBase = new("https://api.playit.gg/");

    /// <summary>playit's tunnel type for Minecraft: Java Edition, which gives a name like "abc.gl.joinmc.link".</summary>
    public const string MinecraftJavaTunnelType = "minecraft-java";

    private readonly Uri _apiBase = apiBase ?? DefaultApiBase;

    /// <summary>The page the user opens to approve this PC.</summary>
    public static Uri ClaimUrl(string code) => new($"https://playit.gg/claim/{Uri.EscapeDataString(code)}");

    /// <summary>A random claim code: 10 hex characters, the form playit's own program uses.</summary>
    public static string NewClaimCode() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));

    /// <summary>Tells playit this PC waits for approval and says how far the user got on the claim page.</summary>
    public async Task<PlayitClaimStatus> ClaimSetupAsync(string code, string version, CancellationToken cancellationToken = default)
    {
        var data = await CallAsync("claim/setup", null, new JsonObject
        {
            ["code"] = code,
            ["agent_type"] = "self-managed",
            ["version"] = version,
        }, cancellationToken).ConfigureAwait(false);

        return AsString(data) switch
        {
            "WaitingForUserVisit" => PlayitClaimStatus.WaitingForVisit,
            "WaitingForUser" => PlayitClaimStatus.WaitingForApproval,
            "UserAccepted" => PlayitClaimStatus.Accepted,
            "UserRejected" => PlayitClaimStatus.Rejected,
            var other => throw new PlayitException(other ?? "Empty"),
        };
    }

    /// <summary>The agent's secret key once the claim was approved; null while playit is still finishing it.</summary>
    public async Task<string?> ClaimExchangeAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await CallAsync("claim/exchange", null, new JsonObject { ["code"] = code }, cancellationToken).ConfigureAwait(false);
            return AsString(data?["secret_key"]);
        }
        catch (PlayitException e) when (e.Code is "CodeNotFound" or "NotAccepted" or "NotSetup" or "UserNotAccepted")
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<PlayitTunnel>> ListTunnelsAsync(string secret, CancellationToken cancellationToken = default)
    {
        var data = await CallAsync("v1/tunnels/list", secret, new JsonObject(), cancellationToken).ConfigureAwait(false);
        return ParseTunnels(data);
    }

    /// <summary>Creates a Minecraft: Java tunnel to <paramref name="localPort"/> on this PC and returns its id.</summary>
    public async Task<Guid> CreateTunnelAsync(string secret, string name, int localPort, CancellationToken cancellationToken = default)
    {
        var data = await CallAsync("v1/tunnels/create", secret, new JsonObject
        {
            ["name"] = name,
            ["ports"] = new JsonObject { ["type"] = "tunnel-type", ["details"] = MinecraftJavaTunnelType },
            ["origin"] = new JsonObject
            {
                ["type"] = "agent",
                ["data"] = new JsonObject { ["agent_id"] = null, ["config"] = Config(localPort) },
            },
            ["enabled"] = true,
            ["alloc"] = null,
            ["firewall_id"] = null,
        }, cancellationToken).ConfigureAwait(false);

        return Guid.TryParse(AsString(data?["id"]), out var id) ? id : throw new PlayitException("NoTunnelId");
    }

    /// <summary>Points an existing tunnel at another port, after the server's port was changed.</summary>
    public async Task SetLocalPortAsync(string secret, Guid tunnelId, int localPort, CancellationToken cancellationToken = default)
    {
        await CallAsync("v1/tunnels/config", secret, new JsonObject
        {
            ["tunnel_id"] = tunnelId.ToString(),
            ["new_agent_id"] = null,
            ["new_config"] = Config(localPort),
        }, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<PlayitTunnel> ParseTunnels(JsonNode? data)
    {
        var tunnels = new List<PlayitTunnel>();
        foreach (var tunnel in data?["tunnels"]?.AsArray().OfType<JsonNode>() ?? [])
        {
            if (!Guid.TryParse(AsString(tunnel["id"]), out var id))
            {
                continue;
            }

            int? localPort = null;
            var origin = tunnel["origin"];
            if (AsString(origin?["type"]) == "agent")
            {
                var port = origin?["details"]?["config_data"]?["fields"]?.AsArray().OfType<JsonNode>()
                    .FirstOrDefault(f => AsString(f["name"]) == "local_port")?["value"];
                if (int.TryParse(AsString(port), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    localPort = parsed;
                }
            }

            var offline = tunnel["offline_reasons"]?.AsArray().Select(AsString).OfType<string>().ToList() ?? [];
            tunnels.Add(new PlayitTunnel(id, AsString(tunnel["name"]), localPort, ConnectAddress(tunnel), offline));
        }

        return tunnels;
    }

    // The name players type: playit's own name ("abc.gl.joinmc.link") first, then a custom domain, then a bare IP.
    private static string? ConnectAddress(JsonNode tunnel)
    {
        var addresses = tunnel["connect_addresses"]?.AsArray().OfType<JsonNode>().ToList() ?? [];
        foreach (var type in new[] { "auto", "domain", "addr4", "ip4" })
        {
            if (addresses.FirstOrDefault(a => AsString(a["type"]) == type)?["value"] is not { } value)
            {
                continue;
            }

            return type switch
            {
                "domain" => AsString(value["domain"]),
                "ip4" => string.Create(CultureInfo.InvariantCulture, $"{AsString(value["address"])}:{value["default_port"]}"),
                _ => AsString(value["address"]),
            };
        }

        return null;
    }

    private static JsonObject Config(int localPort) => new()
    {
        ["fields"] = new JsonArray(
            new JsonObject { ["name"] = "local_ip", ["value"] = "127.0.0.1" },
            new JsonObject { ["name"] = "local_port", ["value"] = localPort.ToString(CultureInfo.InvariantCulture) }),
    };

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private async Task<JsonNode?> CallAsync(string path, string? secret, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_apiBase, path))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (secret is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Agent-Key {secret.Trim()}");
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status == 429)
        {
            throw new PlayitException("TooManyRequests");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new PlayitException(string.Create(CultureInfo.InvariantCulture, $"Http{status}"));
        }

        var data = root?["data"];
        return AsString(root?["status"]) switch
        {
            "success" => data,

            // A refusal is a plain name ("RequiresVerifiedAccount") or {"error": name}; an error is {"type", "message"}.
            "fail" => throw new PlayitException(ErrorName(data, "error")),
            "error" => throw new PlayitException(ErrorName(data, "message")),
            _ => throw new PlayitException(string.Create(CultureInfo.InvariantCulture, $"Http{status}")),
        };
    }

    private static string ErrorName(JsonNode? data, string field) =>
        AsString(data) ?? AsString(data?[field]) ?? AsString(data?["type"]) ?? "Unknown";
}
