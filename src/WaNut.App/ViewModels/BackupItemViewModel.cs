using System.Globalization;
using WaNut.App.Resources;
using WaNut.Core.Backups;

namespace WaNut.App.ViewModels;

/// <summary>One row of the Backups tab.</summary>
public sealed class BackupItemViewModel(BackupInfo info)
{
    public BackupInfo Info { get; } = info;

    /// <summary>
    /// The pattern comes from Strings.resx and is applied with the invariant culture, so the date matches the UI
    /// language rather than the PC's region (an English UI on a Chinese PC would otherwise mix the two). Seconds keep
    /// two backups from the same minute apart.
    /// </summary>
    public string DateText => Info.CreatedAt.ToLocalTime().ToString(Text.Get("Backups_DateFormat"), CultureInfo.InvariantCulture);

    public string ReasonText => Text.Get($"Backups_Reason_{Info.Reason}");

    public string SizeText => FormatSize(Info.SizeBytes);

    /// <summary>What screen readers read for the row.</summary>
    public string AccessibleName => $"{DateText}, {ReasonText}, {SizeText}";

    public string RestoreLabel => string.Format(Text.Get("Backups_RestoreLabel"), DateText, ReasonText);

    public string ExportLabel => string.Format(Text.Get("Backups_ExportLabel"), DateText, ReasonText);

    public override string ToString() => AccessibleName;

    internal static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} GB", bytes / (1024.0 * 1024 * 1024))
        : string.Format(CultureInfo.CurrentCulture, "{0:0.0} MB", bytes / (1024.0 * 1024));
}
