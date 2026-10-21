namespace WaNut.Core.Crashes;

/// <summary>
/// What <see cref="CrashAnalyzer"/> found. <see cref="ModIds"/> are the mods to blame or, for
/// <see cref="CrashCause.MissingMod"/>, the mods to install; <see cref="Files"/> are jar names the output gave;
/// <see cref="Detail"/> is cause-specific: the port, or the Minecraft version or mod a mod asked for.
/// </summary>
public sealed record CrashDiagnosis(CrashCause Cause, IReadOnlyList<string> ModIds, IReadOnlyList<string> Files, string? Detail = null)
{
    public static CrashDiagnosis Of(CrashCause cause, string? detail = null) => new(cause, [], [], detail);

    /// <summary>Starting again without a fix fails the same way, so an automatic restart would only repeat it.</summary>
    public bool WillRepeat => Cause is not (CrashCause.OutOfMemory or CrashCause.ModCrashed);
}
