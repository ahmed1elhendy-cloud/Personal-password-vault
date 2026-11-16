using System.Text;
using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class VaultCodecTests
{
    private const int HeaderLength = 4 + 1 + 16 + (3 * sizeof(int)) + 12;
    private const string TestPassword = "test password";
    private static readonly KdfParams TestKdf = new(1024, 1, 1);

    [Fact]
    public void RoundTrip_EncryptThenDecryptReturnsEqualVaultData()
    {
        var original = CreatePopulatedVault();
        var restored = RoundTrip(original);

        Assert.Equal(VaultJson.Serialize(original), VaultJson.Serialize(restored));
        Assert.Equal("حسابي", restored.Entries[0].Title);
        Assert.Equal("رمز 🔐 hunter2", restored.Entries[0].Password);
    }

    [Fact]
    public void EmptyVault_RoundTrips()
    {
        var original = new VaultData();

        var restored = RoundTrip(original);

        Assert.Equal(VaultJson.Serialize(original), VaultJson.Serialize(restored));
    }

    [Fact]
    public void WrongPassword_ThrowsWrongPasswordException()
    {
        var encrypted = Encrypt(CreatePopulatedVault());
        using var key = VaultCodec.OpenKey("incorrect password", encrypted);

        Assert.Throws<WrongPasswordException>(() => VaultCodec.Decrypt(encrypted, key));
    }

    [Fact]
    public void FlipOneCiphertextByte_ThrowsWrongPasswordException()
    {
        var encrypted = Encrypt(CreatePopulatedVault());
        encrypted[HeaderLength] ^= 0x01;
        using var key = VaultCodec.OpenKey(TestPassword, encrypted);

        Assert.Throws<WrongPasswordException>(() => VaultCodec.Decrypt(encrypted, key));
    }

    [Fact]
    public void FlipOneHeaderByte_ThrowsWrongPasswordException()
    {
        var encrypted = Encrypt(CreatePopulatedVault());
        encrypted[5] ^= 0x01;
        using var key = VaultCodec.OpenKey(TestPassword, encrypted);

        Assert.Throws<WrongPasswordException>(() => VaultCodec.Decrypt(encrypted, key));
    }

    [Fact]
    public void BadMagic_ThrowsInvalidVaultFileException()
    {
        var encrypted = Encrypt(CreatePopulatedVault());
        encrypted[0] ^= 0x01;

        Assert.Throws<InvalidVaultFileException>(() => VaultCodec.OpenKey(TestPassword, encrypted));
    }

    [Fact]
    public void TwoEncryptionsOfSameData_DifferInBytes()
    {
        var data = CreatePopulatedVault();
        using var key = VaultCodec.CreateKey(TestPassword, TestKdf);

        var first = VaultCodec.Encrypt(data, key);
        var second = VaultCodec.Encrypt(data, key);

        Assert.False(first.SequenceEqual(second));
    }

    [Fact]
    public void FileDoesNotContainPlaintext()
    {
        var encrypted = Encrypt(CreatePopulatedVault());
        var plaintext = Encoding.UTF8.GetBytes("hunter2");

        Assert.False(ContainsSubsequence(encrypted, plaintext));
    }

    [Fact]
    public void SameKeyParameters_TrueForSameSaltAndParams_FalseOtherwise()
    {
        var salt = new byte[16];
        var otherSalt = new byte[16];
        otherSalt[0] = 1;
        var otherKdf = new KdfParams(2048, 1, 1);
        using var key = VaultKey.FromKeyBytes(new byte[32], salt, TestKdf);
        using var sameParameters = VaultKey.FromKeyBytes(new byte[32], salt, TestKdf);
        using var differentSalt = VaultKey.FromKeyBytes(new byte[32], otherSalt, TestKdf);
        using var differentKdf = VaultKey.FromKeyBytes(new byte[32], salt, otherKdf);

        Assert.True(key.HasSameParameters(sameParameters));
        Assert.False(key.HasSameParameters(differentSalt));
        Assert.False(key.HasSameParameters(differentKdf));
    }

    private static VaultData RoundTrip(VaultData data)
    {
        var encrypted = Encrypt(data);
        using var key = VaultCodec.OpenKey(TestPassword, encrypted);
        return VaultCodec.Decrypt(encrypted, key);
    }

    private static byte[] Encrypt(VaultData data)
    {
        using var key = VaultCodec.CreateKey(TestPassword, TestKdf);
        return VaultCodec.Encrypt(data, key);
    }

    private static VaultData CreatePopulatedVault()
    {
        var data = VaultData.CreateNew();
        data.Entries.Add(new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = "حسابي",
            Username = "user",
            Password = "رمز 🔐 hunter2",
            Notes = "ملاحظة",
            CreatedAt = DateTimeOffset.Parse("2025-03-04T05:06:07+00:00"),
            UpdatedAt = DateTimeOffset.Parse("2025-03-05T05:06:07+00:00")
        });
        return data;
    }

    private static bool ContainsSubsequence(byte[] source, byte[] value)
    {
        for (var start = 0; start <= source.Length - value.Length; start++)
        {
            if (source.AsSpan(start, value.Length).SequenceEqual(value))
            {
                return true;
            }
        }

        return false;
    }
}
