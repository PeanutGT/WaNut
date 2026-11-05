using WaNut.Core.Java;

namespace WaNut.Core.Remote.Run;

/// <summary>
/// WaNut's own Java on the VPS: one Temurin JRE per major version in ~/wanut/java/java-&lt;major&gt;, downloaded by the
/// VPS itself (plan decision 4A) and checked against Adoptium's SHA-256 before it is unpacked.
/// </summary>
public static class RemoteJava
{
    public const string Folder = "wanut/java";

    /// <summary>The java binary for <paramref name="major"/>, relative to the home folder.</summary>
    public static string JavaPath(int major) => $"{Folder}/java-{major}/bin/java";

    /// <summary>Adoptium's name for the VPS's CPU.</summary>
    public static string AdoptiumArchitecture(HostArchitecture architecture) => architecture switch
    {
        HostArchitecture.X64 => "x64",
        HostArchitecture.Arm64 => "aarch64",
        _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "WaNut can't get Java for this CPU."),
    };

    /// <summary>Prints "yes" when Java <paramref name="major"/> is already there.</summary>
    public static string IsInstalledCommand(int major) =>
        $"if [ -x {RemoteShell.Home(JavaPath(major))} ]; then echo yes; else echo no; fi";

    /// <summary>
    /// Downloads, checks and unpacks <paramref name="package"/> next to its final folder, then renames it, so a
    /// half-unpacked Java is never mistaken for a working one.
    /// </summary>
    public static string InstallCommand(JavaPackage package)
    {
        var final = RemoteShell.Home($"{Folder}/java-{package.Major}");
        var part = RemoteShell.Home($"{Folder}/java-{package.Major}.wanut-part");
        var archive = RemoteShell.Home($"{Folder}/java-{package.Major}.tar.gz.wanut-part");
        var link = RemoteShell.Quote(package.Link.AbsoluteUri);
        var sha = RemoteShell.Quote(package.Sha256.ToLowerInvariant());
        return string.Join('\n',
            "set -e",
            $"rm -rf {part} {archive}",
            $"mkdir -p {part}",
            $"if command -v curl >/dev/null 2>&1; then curl -fsSL -o {archive} {link}; else wget -q -O {archive} {link}; fi",
            $"[ \"$(sha256sum {archive} | cut -d' ' -f1)\" = {sha} ] || {{ rm -f {archive}; echo 'The Java download did not match its checksum.' >&2; exit 3; }}",
            $"tar -xzf {archive} -C {part} --strip-components=1",
            $"rm -f {archive}",
            $"[ -x {part}/bin/java ] || {{ echo 'The Java download has no bin/java.' >&2; exit 4; }}",
            $"rm -rf {final}",
            $"mv {part} {final}");
    }
}
