namespace PasswordVault.Core;

public static class SyncService
{
    private const int MaximumUploadAttempts = 3;

    public static async Task<SyncOutcome> SyncAsync(
        VaultService vault,
        ICloudFileStore cloud,
        string? remotePassword,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(cloud);

        var localData = vault.Data;
        var localFile = vault.ExportFileBytes();

        for (var attempt = 0; attempt < MaximumUploadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remote = await cloud.DownloadAsync(cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                if (await cloud.UploadAsync(localFile, expectedVersion: null, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return SyncOutcome.Uploaded;
                }

                continue;
            }

            if (vault.HasSameKeyParameters(remote.Data))
            {
                var remoteData = vault.DecryptWithCurrentKey(remote.Data);
                var mergedData = VaultMerger.Merge(localData, remoteData);
                var mergedFile = vault.EncryptWithCurrentKey(mergedData);
                if (await cloud.UploadAsync(mergedFile, remote.Version, cancellationToken)
                        .ConfigureAwait(false))
                {
                    vault.CommitSyncedState(mergedData, mergedFile, adoptedKey: null);
                    return SyncOutcome.Merged;
                }

                continue;
            }

            if (remotePassword is null)
            {
                return SyncOutcome.NeedsPassword;
            }

            VaultKey? remoteKey = VaultCodec.OpenKey(remotePassword, remote.Data);
            try
            {
                var remoteData = VaultCodec.Decrypt(remote.Data, remoteKey);
                var mergedData = VaultMerger.Merge(localData, remoteData);
                var mergedFile = VaultCodec.Encrypt(mergedData, remoteKey);
                if (await cloud.UploadAsync(mergedFile, remote.Version, cancellationToken)
                        .ConfigureAwait(false))
                {
                    vault.CommitSyncedState(mergedData, mergedFile, remoteKey);
                    remoteKey = null;
                    return SyncOutcome.Merged;
                }
            }
            finally
            {
                remoteKey?.Dispose();
            }
        }

        return SyncOutcome.Failed;
    }
}
