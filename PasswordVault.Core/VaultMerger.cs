namespace PasswordVault.Core;

public static class VaultMerger
{
    public static VaultData Merge(VaultData local, VaultData remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        return new VaultData
        {
            SchemaVersion = local.SchemaVersion,
            Entries = MergeItems(
                local.Entries,
                remote.Entries,
                entry => entry.Id,
                entry => entry.UpdatedAt,
                entry => entry.IsDeleted),
            Categories = MergeItems(
                local.Categories,
                remote.Categories,
                category => category.Id,
                category => category.UpdatedAt,
                category => category.IsDeleted),
            Settings = local.Settings
        };
    }

    private static List<T> MergeItems<T>(
        IReadOnlyList<T> localItems,
        IReadOnlyList<T> remoteItems,
        Func<T, Guid> getId,
        Func<T, DateTimeOffset> getUpdatedAt,
        Func<T, bool> isDeleted)
    {
        var localById = localItems
            .GroupBy(getId)
            .ToDictionary(group => group.Key, group => group.First());
        var remoteById = remoteItems
            .GroupBy(getId)
            .ToDictionary(group => group.Key, group => group.First());
        var merged = new List<T>(localById.Count + remoteById.Count);
        var addedIds = new HashSet<Guid>();

        foreach (var localItem in localItems)
        {
            var id = getId(localItem);
            if (!addedIds.Add(id))
            {
                continue;
            }

            if (remoteById.TryGetValue(id, out var remoteItem))
            {
                merged.Add(ChooseWinner(localItem, remoteItem, getUpdatedAt, isDeleted));
            }
            else
            {
                merged.Add(localItem);
            }
        }

        foreach (var remoteItem in remoteItems)
        {
            if (addedIds.Add(getId(remoteItem)))
            {
                merged.Add(remoteItem);
            }
        }

        return merged;
    }

    private static T ChooseWinner<T>(
        T local,
        T remote,
        Func<T, DateTimeOffset> getUpdatedAt,
        Func<T, bool> isDeleted)
    {
        var timestampComparison = getUpdatedAt(remote).CompareTo(getUpdatedAt(local));
        if (timestampComparison > 0)
        {
            return remote;
        }

        if (timestampComparison < 0)
        {
            return local;
        }

        var localIsDeleted = isDeleted(local);
        var remoteIsDeleted = isDeleted(remote);
        return localIsDeleted == remoteIsDeleted || localIsDeleted
            ? local
            : remote;
    }
}
