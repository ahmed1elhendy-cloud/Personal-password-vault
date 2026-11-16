using System.Security.Cryptography;

namespace PasswordVault.Core;

public sealed class VaultKey : IDisposable
{
    private byte[]? _keyBytes;
    private readonly byte[] _salt;

    private VaultKey(byte[] key, byte[] salt, KdfParams kdf)
    {
        _keyBytes = key.ToArray();
        _salt = salt.ToArray();
        Kdf = kdf;
    }

    public byte[] Salt => _salt.ToArray();
    public KdfParams Kdf { get; }

    public byte[] ExportKeyBytes()
    {
        ObjectDisposedException.ThrowIf(_keyBytes is null, this);
        return _keyBytes.ToArray();
    }

    public bool HasSameParameters(VaultKey other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return HasSameParameters(other._salt, other.Kdf);
    }

    public static VaultKey FromKeyBytes(byte[] key, byte[] salt, KdfParams kdf)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(kdf);
        if (key.Length != VaultCodec.KeyLength)
        {
            throw new ArgumentException($"A vault key must be {VaultCodec.KeyLength} bytes.", nameof(key));
        }

        if (salt.Length != VaultCodec.SaltLength)
        {
            throw new ArgumentException($"A vault salt must be {VaultCodec.SaltLength} bytes.", nameof(salt));
        }

        return new VaultKey(key, salt, kdf);
    }

    internal ReadOnlySpan<byte> KeyBytes
    {
        get
        {
            return _keyBytes ?? throw new ObjectDisposedException(nameof(VaultKey));
        }
    }

    internal ReadOnlySpan<byte> SaltBytes => _salt;

    internal bool HasSameParameters(ReadOnlySpan<byte> salt, KdfParams kdf)
    {
        return Kdf == kdf && CryptographicOperations.FixedTimeEquals(_salt, salt);
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
