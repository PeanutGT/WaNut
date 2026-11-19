using System.Globalization;

namespace WaNut.Bridge.Relay;

/// <summary>The bridge's own words in Discord and in the bot status, in the language from the config.</summary>
public sealed class BridgeTexts
{
    public static BridgeTexts English { get; } = new()
    {
        ServerStarted = "✅ Server started",
        ServerStopping = "🛑 Server stopping",
        ServerOffline = "The server is offline.",
        PresenceOffline = "Server offline",
        PresenceOnline = "Server online",
        ConsoleDenied = "Only members with the admin role can use /cmd.",
        ConsoleNoOutput = "(no output)",
        StatusDenied = "Only members with the admin role can use /status.",
        StatusCreatedFormat = "The live status message is now in <#{0}>. It updates every minute.",
        StatusAlreadyHereFormat = "The live status message is already in <#{0}>. Refreshed it. A server has only one.",
        StatusMovedFormat = "Moved the live status message to <#{0}>. A server has only one, so the old one was removed.",
        StatusFailedFormat = "Could not post in <#{0}>. Give the bot View Channel, Send Messages and Embed Links there.",
        WrongChannel = "This command only works in the bridge channel.",
        JoinedFormat = "{0} joined the game",
        LeftFormat = "{0} left the game",
        OnlineFormat = "Online ({0}/{1}): {2}",
        NobodyFormat = "Nobody is online (0/{0}).",
        PresenceFormat = "{0} / {1} online",
        ImageFormat = "[Image: {0}]",
        FileFormat = "[File: {0}]",
        OpenHint = "Click to open in your browser",
        MoreFormat = "+{0} more on Discord",
        ReplyFormat = "(reply to {0})",
    };

    public static BridgeTexts TraditionalChinese { get; } = new()
    {
        Chinese = true,
        ServerStarted = "✅ 伺服器已啟動",
        ServerStopping = "🛑 伺服器正在關閉",
        ServerOffline = "伺服器目前沒有開。",
        PresenceOffline = "伺服器未開啟",
        PresenceOnline = "伺服器已開啟",
        ConsoleDenied = "只有管理員身分組的成員可以使用 /cmd。",
        ConsoleNoOutput = "（沒有輸出）",
        StatusDenied = "只有管理員身分組的成員可以使用 /status。",
        StatusCreatedFormat = "自動更新的狀態訊息已放在 <#{0}>，每分鐘更新一次。",
        StatusAlreadyHereFormat = "自動更新的狀態訊息本來就在 <#{0}>，已重新整理。每個伺服器只會有一則。",
        StatusMovedFormat = "已把自動更新的狀態訊息搬到 <#{0}>。每個伺服器只會有一則，舊的已刪除。",
        StatusFailedFormat = "無法在 <#{0}> 發訊息。請讓機器人在該頻道有「檢視頻道」「傳送訊息」「嵌入連結」權限。",
        WrongChannel = "這個指令只能在橋接頻道使用。",
        JoinedFormat = "{0} 加入了遊戲",
        LeftFormat = "{0} 離開了遊戲",
        OnlineFormat = "線上（{0}/{1}）：{2}",
        NobodyFormat = "目前沒有人在線（0/{0}）。",
        PresenceFormat = "{0} / {1} 人在線",
        ImageFormat = "[圖片：{0}]",
        FileFormat = "[檔案：{0}]",
        OpenHint = "點擊在瀏覽器開啟",
        MoreFormat = "還有 {0} 個，請到 Discord 查看",
        ReplyFormat = "（回覆 {0}）",
    };

    public bool Chinese { get; init; }

    public required string ServerStarted { get; init; }

    public required string ServerStopping { get; init; }

    public required string ServerOffline { get; init; }

    public required string PresenceOffline { get; init; }

    public required string PresenceOnline { get; init; }

    public required string ConsoleDenied { get; init; }

    public required string ConsoleNoOutput { get; init; }

    public required string StatusDenied { get; init; }

    public required string StatusCreatedFormat { get; init; }

    public required string StatusAlreadyHereFormat { get; init; }

    public required string StatusMovedFormat { get; init; }

    public required string StatusFailedFormat { get; init; }

    public required string WrongChannel { get; init; }

    public required string JoinedFormat { get; init; }

    public required string LeftFormat { get; init; }

    public required string OnlineFormat { get; init; }

    public required string NobodyFormat { get; init; }

    public required string PresenceFormat { get; init; }

    public required string ImageFormat { get; init; }

    public required string FileFormat { get; init; }

    public required string OpenHint { get; init; }

    public required string MoreFormat { get; init; }

    public required string ReplyFormat { get; init; }

    /// <summary>"zh-TW", "zh-Hant" or "zh" give Traditional Chinese; anything else English.</summary>
    public static BridgeTexts For(string? language) =>
        language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ? TraditionalChinese : English;

    public string Joined(string player) => Format(JoinedFormat, player);

    public string Left(string player) => Format(LeftFormat, player);

    public string Online(PlayerList list) =>
        list.Names.Count == 0 ? Format(NobodyFormat, list.Max) : Format(OnlineFormat, list.Online, list.Max, string.Join(", ", list.Names));

    public string StatusPlaced(StatusPlacement placement, ulong channelId) => Format(
        placement switch
        {
            StatusPlacement.AlreadyHere => StatusAlreadyHereFormat,
            StatusPlacement.Moved => StatusMovedFormat,
            _ => StatusCreatedFormat,
        },
        channelId);

    public string StatusFailed(ulong channelId) => Format(StatusFailedFormat, channelId);

    public string Presence(PlayerList list) => Format(PresenceFormat, list.Online, list.Max);

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
}
