namespace WaNut.Core.Instances;

public static class EulaFile
{
    public const string FileName = "eula.txt";
    public static readonly Uri EulaUrl = new("https://aka.ms/MinecraftEULA");

    /// <summary>
    /// Records that the user accepted the Minecraft EULA in WaNut. Only call this after they ticked the box.
    /// </summary>
    public static void WriteAccepted(string instanceDir, DateTimeOffset acceptedAt)
    {
        var contents =
            $"#By changing the setting below to TRUE you are indicating your agreement to our EULA ({EulaUrl}).{Environment.NewLine}" +
            $"#Accepted in WaNut on {acceptedAt:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
            $"eula=true{Environment.NewLine}";
        AtomicFile.WriteAllText(Path.Combine(instanceDir, FileName), contents);
    }

    public static bool IsAccepted(string instanceDir)
    {
        var path = Path.Combine(instanceDir, FileName);
        return File.Exists(path)
            && File.ReadLines(path).Any(line => line.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase));
    }
}
