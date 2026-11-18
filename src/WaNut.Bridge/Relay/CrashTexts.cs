using WaNut.Core.Crashes;

namespace WaNut.Bridge.Relay;

/// <summary>What the crash alert says: the likely cause and one thing to try, in English or Traditional Chinese.</summary>
public static class CrashTexts
{
    public static string Alert(CrashDiagnosis? diagnosis, bool chinese)
    {
        var head = chinese ? "💥 伺服器崩潰了" : "💥 The server crashed";
        if (diagnosis is null)
        {
            return head + "\n" + (chinese
                ? "原因不明。在 WaNut 的 Console 或 crash-reports 資料夾看最後幾行。"
                : "The cause is unknown. Check the last lines in WaNut's Console or the crash-reports folder.");
        }

        var mods = string.Join(", ", diagnosis.ModIds.Concat(diagnosis.Files));
        var detail = diagnosis.Detail ?? string.Empty;
        var (cause, fix) = diagnosis.Cause switch
        {
            CrashCause.PortInUse => Pick(chinese,
                $"連接埠 {detail} 已被占用，通常是另一台伺服器還在跑。", "關掉另一個程式，或在 WaNut 換連接埠。",
                $"Port {detail} is already in use, often by a server that is still running.", "Close the other program or pick another port in WaNut."),
            CrashCause.DuplicateMod => Pick(chinese,
                $"Mod 重複：{mods}。", "留下新的一份，其他移走。",
                $"A mod is installed twice: {mods}.", "Keep the newest copy and move the others out."),
            CrashCause.MissingMod => Pick(chinese,
                $"缺少必要的 mod：{mods}。", "在 WaNut 的 Mods 分頁安裝它。",
                $"A required mod is missing: {mods}.", "Install it from WaNut's Mods tab."),
            CrashCause.IncompatibleMod => Pick(chinese,
                $"Mod 與版本或載入器不合：{mods}。", "更新或停用它。",
                $"A mod does not fit this version or loader: {mods}.", "Update or turn it off."),
            CrashCause.ClientOnlyMod => Pick(chinese,
                $"只能在玩家端用的 mod 放到了伺服器：{mods}。", "從伺服器停用它。",
                $"A client-only mod is on the server: {mods}.", "Turn it off on the server."),
            CrashCause.OutOfMemory => Pick(chinese,
                "記憶體用完了。", "在 Server 分頁加大記憶體，或減少 mod 與視距。",
                "The server ran out of memory.", "Give it more memory, or use fewer mods or a shorter view distance."),
            CrashCause.MemoryTooHigh => Pick(chinese,
                "設定的記憶體比這台機器能給的還多。", "降低記憶體設定再啟動。",
                "The memory setting is more than the machine can give.", "Lower it and start again."),
            CrashCause.WrongJava => Pick(chinese,
                $"Java 版本不符（需要 Java {detail}）。", "在 WaNut 檢查伺服器的類型與版本。",
                $"The Java version does not fit (needs Java {detail}).", "Check the server's type and version in WaNut."),
            CrashCause.BrokenWorld => Pick(chinese,
                "世界檔案讀不了，可能已損壞。", "到 Backups 分頁還原備份。",
                "The world files cannot be read and may be damaged.", "Restore a backup from the Backups tab."),
            _ => Pick(chinese,
                mods.Length > 0 ? $"有 mod 崩潰：{mods}。" : "有 mod 崩潰。", "更新或停用它再啟動。",
                mods.Length > 0 ? $"A mod crashed: {mods}." : "A mod crashed.", "Update or turn it off, then start again."),
        };

        return $"{head}\n{cause}\n{(chinese ? "試試：" : "Try: ")}{fix}";
    }

    private static (string, string) Pick(bool chinese, string zhCause, string zhFix, string enCause, string enFix) =>
        chinese ? (zhCause, zhFix) : (enCause, enFix);
}
