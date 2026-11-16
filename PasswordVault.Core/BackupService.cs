namespace PasswordVault.Core;

public static class BackupService
{
    public static byte[] ExportFileBytes(VaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        return vault.ExportFileBytes();
    }

    public static int Import(VaultService vault, byte[] backupFile, string backupPassword)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(backupFile);
        ArgumentNullException.ThrowIfNull(backupPassword);

        var localData = vault.Data;
        VaultData remoteData;
        using (var backupKey = VaultCodec.OpenKey(backupPassword, backupFile))
        {
            remoteData = VaultCodec.Decrypt(backupFile, backupKey);
        }

        var mergedData = VaultMerger.Merge(localData, remoteData);
        var localEntriesById = localData.Entries
            .GroupBy(entry => entry.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var changedEntries = mergedData.Entries.Count(entry =>
            !localEntriesById.TryGetValue(entry.Id, out var localEntry)
            || !ReferenceEquals(entry, localEntry));

        vault.ReplaceDataAndSave(mergedData);
        return changedEntries;
    }
}
