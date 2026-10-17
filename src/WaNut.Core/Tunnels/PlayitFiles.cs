using System.Text.RegularExpressions;
using WaNut.Core.Instances;

namespace WaNut.Core.Tunnels;

/// <summary>
/// WaNut's own copy of the playit.gg agent and its secret key, in playit\ under the data folder. A playit program the
/// user installed separately keeps its own key and is never touched.
/// </summary>
public sealed partial class PlayitFiles(LauncherPaths paths)
{
    public string Dir { get; } = Path.Combine(paths.Root, "playit");

    /// <summary>The key playit gave this PC when the user approved it, in the playit.toml form the agent reads.</summary>
    public string SecretFile => Path.Combine(Dir, "playit.toml");

    public string LogFile => Path.Combine(Dir, "playitd.log");

    /// <summary>The agent of <paramref name="version"/> ("v1.0.10"), kept under its version so an update never runs a half-written file.</summary>
    public string AgentExe(string version) => Path.Combine(Dir, $"playitd-{version}.exe");

    /// <summary>The newest agent already downloaded, or null.</summary>
    public string? FindAgentExe() => Directory.Exists(Dir)
        ? Directory.EnumerateFiles(Dir, "playitd-*.exe").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;

    public bool HasSecret => LoadSecret() is not null;

    public string? LoadSecret()
    {
        try
        {
            var match = SecretLine().Match(File.ReadAllText(SecretFile));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void SaveSecret(string secret)
    {
        if (!SecretValue().IsMatch(secret))
        {
            throw new ArgumentException("A playit secret key is hex text.", nameof(secret));
        }

        Directory.CreateDirectory(Dir);
        AtomicFile.WriteAllText(SecretFile, $"secret_key = \"{secret}\"\n");
    }

    /// <summary>"Forget this playit account": the next use asks for approval in the browser again.</summary>
    public void ForgetSecret() => File.Delete(SecretFile);

    [GeneratedRegex("""^\s*secret_key\s*=\s*"([0-9a-fA-F]+)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex SecretLine();

    [GeneratedRegex("^[0-9a-fA-F]+$")]
    private static partial Regex SecretValue();
}
