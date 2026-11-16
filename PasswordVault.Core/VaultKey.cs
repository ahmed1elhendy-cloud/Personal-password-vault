using System.Security.Cryptography;

namespace PasswordVault.Core;

public sealed class VaultKey : IDisposable
{
    private byte[]? _keyBytes;

    private VaultKey(byte[] key, byte[] salt, KdfParams kdf)
    {
        _keyBytes = key.ToArray();
        Salt = salt.ToArray();
        Kdf = kdf;
    }

    public byte[] Salt { get; }
    public KdfParams Kdf { get; }

    public byte[] ExportKeyBytes()
    {
        ObjectDisposedException.ThrowIf(_keyBytes is null, this);
        return _keyBytes.ToArray();
    }

    public static VaultKey FromKeyBytes(byte[] key, byte[] salt, KdfParams kdf)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(kdf);
        return new VaultKey(key, salt, kdf);
    }

    public void Dispose()
    {
        if (_keyBytes is not null)
        {
            CryptographicOperations.ZeroMemory(_keyBytes);
            _keyBytes = null;
        }
    }
}
