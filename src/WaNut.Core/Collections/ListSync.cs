using System.Collections.ObjectModel;

namespace WaNut.Core.Collections;

/// <summary>
/// Makes an observable list match a new set of values by adding, removing and moving only what changed, instead of
/// clearing and refilling it. The screen can then animate the rows that really came or went (docs/design.md,
/// section 6), and rows that stayed keep their state, such as a player's loaded face.
/// </summary>
public static class ListSync
{
    /// <summary>
    /// Updates <paramref name="target"/> so it holds one item per distinct key of <paramref name="source"/>, in the
    /// source's order. Items whose key is still there stay (moved if needed); <paramref name="refresh"/> lets them
    /// pick up new values in place. When <paramref name="isCurrent"/> says an item no longer matches its source, it
    /// is replaced with a new one. New keys get <paramref name="create"/>.
    /// </summary>
    public static void Sync<TItem, TSource, TKey>(
        ObservableCollection<TItem> target,
        IEnumerable<TSource> source,
        Func<TSource, TKey> sourceKey,
        Func<TItem, TKey> itemKey,
        Func<TSource, TItem> create,
        IEqualityComparer<TKey>? comparer = null,
        Func<TItem, TSource, bool>? isCurrent = null,
        Action<TItem, TSource>? refresh = null)
        where TKey : notnull
    {
        comparer ??= EqualityComparer<TKey>.Default;
        var wanted = new List<(TKey Key, TSource Value)>();
        var wantedKeys = new HashSet<TKey>(comparer);
        foreach (var value in source)
        {
            var key = sourceKey(value);
            if (wantedKeys.Add(key))
            {
                wanted.Add((key, value));
            }
        }

        // Items that are gone, and second copies of a key, go first, so what is left only needs moving.
        var seen = new HashSet<TKey>(comparer);
        for (var i = 0; i < target.Count;)
        {
            var key = itemKey(target[i]);
            if (!wantedKeys.Contains(key) || !seen.Add(key))
            {
                target.RemoveAt(i);
                continue;
            }

            i++;
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var (key, value) = wanted[i];
            var found = -1;
            for (var j = i; j < target.Count; j++)
            {
                if (comparer.Equals(itemKey(target[j]), key))
                {
                    found = j;
                    break;
                }
            }

            if (found < 0)
            {
                target.Insert(i, create(value));
                continue;
            }

            if (found != i)
            {
                target.Move(found, i);
            }

            if (isCurrent is not null && !isCurrent(target[i], value))
            {
                target[i] = create(value);
            }
            else
            {
                refresh?.Invoke(target[i], value);
            }
        }
    }

    /// <summary>The same for a list of plain values that are their own key, such as player names.</summary>
    public static void Sync<T>(ObservableCollection<T> target, IEnumerable<T> source, IEqualityComparer<T>? comparer = null)
        where T : notnull =>
        Sync(target, source, v => v, v => v, v => v, comparer);
}
