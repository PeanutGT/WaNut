using WaNut.App.Resources;
using WaNut.Core.Workspace;

namespace WaNut.App.ViewModels;

/// <summary>One bar of the Files tab's capacity chart.</summary>
public sealed class UsageRowViewModel(CategoryUsage usage, double fraction)
{
    public CategoryUsage Usage => usage;

    public double Fraction => fraction;

    public string Name => Text.Get($"Files_Category_{usage.Category}");

    public string SizeText => ByteSize.Format(usage.Bytes);

    public string AccessibleName => $"{Name}, {SizeText}, {fraction:P0}";

    public override string ToString() => AccessibleName;
}
