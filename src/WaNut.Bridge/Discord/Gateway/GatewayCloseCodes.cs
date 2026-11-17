namespace WaNut.Bridge.Discord.Gateway;

/// <summary>What a Gateway close code means for the next connection.</summary>
public static class GatewayCloseCodes
{
    public const int AuthenticationFailed = 4004;
    public const int InvalidSequence = 4007;
    public const int SessionTimedOut = 4009;
    public const int DisallowedIntents = 4014;

    /// <summary>Codes after which trying again cannot help until the user changes something.</summary>
    public static bool IsFatal(int? code) => code is AuthenticationFailed or 4010 or 4011 or 4012 or 4013 or DisallowedIntents;

    /// <summary>Codes after which the old session cannot be resumed, so the next connection identifies afresh.</summary>
    public static bool EndsSession(int? code) => code is InvalidSequence or SessionTimedOut;

    public static string Explain(int? code) => code switch
    {
        AuthenticationFailed => "Discord refused the bot token.",
        DisallowedIntents => "Discord refused the Message Content intent. Turn it on for the bot in the Discord developer portal (Bot > Privileged Gateway Intents).",
        _ => $"Discord closed the connection for good (code {code}).",
    };
}
