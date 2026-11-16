using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace PasswordVault.Core;

public static class VaultCodec
{
    private const byte CurrentVersion = 1;
    private const int MagicOffset = 0;
    private const int VersionOffset = 4;
    private const int SaltOffset = 5;
    private const int MemoryOffset = SaltOffset + SaltLength;
    private const int IterationsOffset = MemoryOffset + sizeof(int);
    private const int ParallelismOffset = IterationsOffset + sizeof(int);
    private const int NonceOffset = ParallelismOffset + sizeof(int);
    private const int HeaderLength = NonceOffset + NonceLength;
    private static ReadOnlySpan<byte> Magic => "PVLT"u8;

    internal const int KeyLength = 32;
    internal const int SaltLength = 16;
    internal const int NonceLength = 12;
    internal const int TagLength = 16;

    public static VaultKey CreateKey(string password)
    {
        return CreateKey(password, KdfParams.Default);
    }

    internal static VaultKey CreateKey(string password, KdfParams parameters)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(parameters);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        return DeriveKey(password, salt, parameters);
    }

    public static VaultKey OpenKey(string password, ReadOnlySpan<byte> vaultFile)
    {
        ArgumentNullException.ThrowIfNull(password);

        var header = ParseHeader(vaultFile);
        return DeriveKey(password, header.Salt, header.Kdf);
    }

    public static bool SameKeyParameters(VaultKey key, ReadOnlySpan<byte> vaultFile)
    {
        ArgumentNullException.ThrowIfNull(key);
        var header = ParseHeader(vaultFile);
        return key.HasSameParameters(header.Salt, header.Kdf);
    }

    public static byte[] Encrypt(VaultData data, VaultKey key)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);

        var plaintext = VaultJson.Serialize(data);
        try
        {
            var file = new byte[HeaderLength + plaintext.Length + TagLength];
            var header = file.AsSpan(0, HeaderLength);
            Magic.CopyTo(header[MagicOffset..]);
            header[VersionOffset] = CurrentVersion;
            key.SaltBytes.CopyTo(header.Slice(SaltOffset, SaltLength));
            BinaryPrimitives.WriteInt32LittleEndian(header[MemoryOffset..], key.Kdf.MemoryKb);
            BinaryPrimitives.WriteInt32LittleEndian(header[IterationsOffset..], key.Kdf.Iterations);
            BinaryPrimitives.WriteInt32LittleEndian(header[ParallelismOffset..], key.Kdf.Parallelism);

            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            nonce.CopyTo(header[NonceOffset..]);
            var ciphertext = file.AsSpan(HeaderLength, plaintext.Length);
            var tag = file.AsSpan(HeaderLength + plaintext.Length, TagLength);

            using var aes = new AesGcm(key.KeyBytes, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, header);

            return file;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static VaultData Decrypt(ReadOnlySpan<byte> vaultFile, VaultKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var header = ParseHeader(vaultFile);
        var ciphertextLength = vaultFile.Length - HeaderLength - TagLength;
        var plaintext = new byte[ciphertextLength];

        try
        {
            try
            {
                using var aes = new AesGcm(key.KeyBytes, TagLength);
                aes.Decrypt(
                    vaultFile.Slice(NonceOffset, NonceLength),
                    vaultFile.Slice(HeaderLength, ciphertextLength),
                    vaultFile[^TagLength..],
                    plaintext,
                    vaultFile[..HeaderLength]);
            }
            catch (AuthenticationTagMismatchException exception)
            {
                throw new WrongPasswordException(exception);
            }

            return VaultJson.Deserialize(plaintext);
        }
        catch (JsonException exception)
        {
            throw new InvalidVaultFileException("The decrypted vault contents are invalid.", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidVaultFileException("The decrypted vault contents are not valid UTF-8.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static VaultKey DeriveKey(string password, byte[] salt, KdfParams parameters)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? derivedKey = null;
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemoryKb,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism
            };
            derivedKey = argon2.GetBytes(KeyLength);
            return VaultKey.FromKeyBytes(derivedKey, salt, parameters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (derivedKey is not null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }
        }
    }

    private static ParsedHeader ParseHeader(ReadOnlySpan<byte> vaultFile)
    {
        if (vaultFile.Length < HeaderLength + TagLength)
        {
            throw new InvalidVaultFileException("The vault file is too short.");
        }

        if (!vaultFile[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidVaultFileException("The vault file has an invalid signature.");
        }

        if (vaultFile[VersionOffset] != CurrentVersion)
        {
            throw new InvalidVaultFileException("The vault file version is not supported.");
        }

        KdfParams kdf;
        try
        {
            kdf = new KdfParams(
                BinaryPrimitives.ReadInt32LittleEndian(vaultFile[MemoryOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(vaultFile[IterationsOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(vaultFile[ParallelismOffset..]));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidVaultFileException("The vault file contains invalid KDF parameters.", exception);
        }

        return new ParsedHeader(vaultFile.Slice(SaltOffset, SaltLength).ToArray(), kdf);
    }

    private sealed record ParsedHeader(byte[] Salt, KdfParams Kdf);
}
