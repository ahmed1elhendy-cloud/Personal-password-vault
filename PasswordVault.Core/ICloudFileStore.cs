namespace PasswordVault.Core;

public interface ICloudFileStore
{
    Task<RemoteFile?> DownloadAsync(CancellationToken cancellationToken);
    Task<bool> UploadAsync(byte[] data, string? expectedVersion, CancellationToken cancellationToken);
}
