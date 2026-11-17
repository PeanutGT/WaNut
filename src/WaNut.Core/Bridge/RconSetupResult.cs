namespace WaNut.Core.Bridge;

/// <param name="Port">The RCON port the server will listen on.</param>
/// <param name="Password">The RCON password now in server.properties.</param>
/// <param name="RestartNeeded">server.properties changed, so a running server still has the old values until it restarts.</param>
/// <param name="PasswordGenerated">The file had no password and a new one was made.</param>
public sealed record RconSetupResult(int Port, string Password, bool RestartNeeded, bool PasswordGenerated);
