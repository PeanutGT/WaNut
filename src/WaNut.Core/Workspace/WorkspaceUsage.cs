using WaNut.Core.Storage;

namespace WaNut.Core.Workspace;

/// <summary>How much of a server folder one <see cref="McFileCategory"/> takes.</summary>
public sealed record CategoryUsage(McFileCategory Category, long Bytes, int Files);

/// <summary>The size of a server folder split by what the files are for, biggest first, for the capacity bar.</summary>
public sealed record WorkspaceUsage(long TotalBytes, IReadOnlyList<CategoryUsage> Categories)
{
    public static readonly WorkspaceUsage Empty = new(0, []);

    /// <summary>Adds up the files in a recursive listing; folders hold no size of their own.</summary>
    public static WorkspaceUsage From(IEnumerable<ServerFileNode> nodes)
    {
        var categories = nodes
            .Where(n => !n.IsDirectory)
            .GroupBy(n => n.Category)
            .Select(g => new CategoryUsage(g.Key, g.Sum(n => n.SizeBytes), g.Count()))
            .OrderByDescending(c => c.Bytes)
            .ThenBy(c => c.Category)
            .ToList();
        return new WorkspaceUsage(categories.Sum(c => c.Bytes), categories);
    }

    /// <summary>The share of the folder one category takes, 0 to 1.</summary>
    public double Fraction(CategoryUsage category) => TotalBytes == 0 ? 0 : (double)category.Bytes / TotalBytes;
}
