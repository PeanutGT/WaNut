using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Workspace;

namespace WaNut.App.ViewModels;

/// <summary>One checkable line of the Files tab's clean-up preview.</summary>
public sealed partial class CleanupItemViewModel(CleanupItem item, Action changed) : ObservableObject
{
    public CleanupItem Item => item;

    [ObservableProperty]
    private bool _isChecked = item.CheckedByDefault;

    public string Path => item.Path;

    public bool Risky => item.Risky;

    public string ReasonText => Text.Get($"Files_CleanupReason_{item.Reason}");

    public string DetailText =>
        $"{ReasonText} · {ByteSize.Format(item.SizeBytes)} · {item.Node.LastModifiedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}";

    public string? RiskText => item.Risky ? Text.Get("Files_CleanupRiskItem") : null;

    public string AccessibleName => item.Risky ? $"{Path}, {DetailText}, {RiskText}" : $"{Path}, {DetailText}";

    partial void OnIsCheckedChanged(bool value) => changed();

    public override string ToString() => AccessibleName;
}
