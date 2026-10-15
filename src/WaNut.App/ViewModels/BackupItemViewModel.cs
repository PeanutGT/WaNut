using System.Globalization;
using WaNut.App.Resources;
using WaNut.Core.Backups;

namespace WaNut.App.ViewModels;

/// <summary>One row of the Backups tab.</summary>
public sealed class BackupItemViewModel(BackupInfo info)
{
    public BackupInfo Info { get; } = info;

    public string DateText => Info.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ReasonText => Text.Get($"Backups_Reason_{Info.Reason}");

    public string SizeText => FormatSize(Info.SizeBytes);

    /// <summary>What screen readers read for the row.</summary>
    public string AccessibleName => $"{DateText}, {ReasonText}, {SizeText}";

    public string RestoreLabel => string.Format(Text.Get("Backups_RestoreLabel"), DateText);

    public override string ToString() => AccessibleName;

    internal static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} GB", bytes / (1024.0 * 1024 * 1024))
        : string.Format(CultureInfo.CurrentCulture, "{0:0.0} MB", bytes / (1024.0 * 1024));
}
