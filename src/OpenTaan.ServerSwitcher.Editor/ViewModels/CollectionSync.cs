using System.Collections.ObjectModel;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

internal static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="target"/> equal to <paramref name="desired"/> with minimal Move/Insert/Remove calls,
    /// so existing items (and their UI containers, focus and expansion) are kept.
    /// </summary>
    public static void SyncTo<T>(this ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], item))
            {
                continue;
            }

            var existing = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], item))
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, item);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
