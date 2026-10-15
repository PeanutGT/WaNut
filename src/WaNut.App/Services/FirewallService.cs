using WaNut.App.Resources;
using WaNut.App.ViewModels;
using WaNut.Core.Java;
using WaNut.Core.Network;

namespace WaNut.App.Services;

/// <summary>
/// Lets WaNut's java.exe through Windows Defender Firewall (blueprint 2). Without the rule Windows can silently drop
/// friends, on the home network and on Radmin VPN alike. The rule is per java.exe, since rules name a program file.
/// </summary>
public sealed class FirewallService(IFirewall firewall, JavaRuntimeManager java, AppSettingsViewModel app, IDialogService dialogs)
{
    public static string RuleName(int javaMajor) => $"WaNut - Java {javaMajor}";

    public string? FindJavaExe(int javaMajor) => java.FindJavaExe(javaMajor);

    public Task<FirewallState> GetStateAsync(string javaExe) => firewall.GetStateAsync(javaExe);

    public Task<FirewallChange> AllowAsync(string javaExe, int javaMajor) => firewall.AllowAsync(javaExe, RuleName(javaMajor));

    /// <summary>
    /// Before the first start with each Java: if friends would be blocked, explains why and asks once, then shows the
    /// UAC prompt. Returns what changed, or null when nothing was asked. Declining never stops the server starting.
    /// </summary>
    public async Task<FirewallChange?> AskOnceAsync(string javaExe, int javaMajor)
    {
        if (app.WasFirewallAsked(javaExe))
        {
            return null;
        }

        var state = await firewall.GetStateAsync(javaExe);
        if (state == FirewallState.Unknown)
        {
            // Try again next start rather than never asking.
            return null;
        }

        app.MarkFirewallAsked(javaExe);
        if (state == FirewallState.Allowed)
        {
            return null;
        }

        var question = state == FirewallState.Blocked ? "Firewall_AskBlocked" : "Firewall_Ask";
        if (!dialogs.Confirm(string.Format(Text.Get(question), javaMajor)))
        {
            return FirewallChange.Declined;
        }

        return await firewall.AllowAsync(javaExe, RuleName(javaMajor));
    }
}
