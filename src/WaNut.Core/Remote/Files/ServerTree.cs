using WaNut.Core.Storage;

namespace WaNut.Core.Remote.Files;

/// <summary>Puts a flat listing in the order <see cref="LocalFileSystemAdapter"/> walks a folder, so both list alike.</summary>
internal static class ServerTree
{
    /// <summary>Each folder followed by what is in it; within a folder, folders first and then by name.</summary>
    public static IReadOnlyList<ServerFileNode> Sort(IEnumerable<ServerFileNode> nodes)
    {
        var list = nodes.ToList();
        list.Sort(Compare);
        return list;
    }

    private static int Compare(ServerFileNode a, ServerFileNode b)
    {
        var left = a.Path.Split('/');
        var right = b.Path.Split('/');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            if (string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                continue;
            }

            // A segment before the last is a folder by definition.
            var leftIsFolder = i < left.Length - 1 || a.IsDirectory;
            var rightIsFolder = i < right.Length - 1 || b.IsDirectory;
            if (leftIsFolder != rightIsFolder)
            {
                return leftIsFolder ? -1 : 1;
            }

            var byName = StringComparer.OrdinalIgnoreCase.Compare(left[i], right[i]);
            return byName != 0 ? byName : string.CompareOrdinal(left[i], right[i]);
        }

        return left.Length.CompareTo(right.Length);
    }
}
