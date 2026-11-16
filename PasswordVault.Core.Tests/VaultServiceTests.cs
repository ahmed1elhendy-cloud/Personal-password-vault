using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class VaultServiceTests
{
    private const string Password = "correct horse battery";
    private const string NewPassword = "new correct horse";
    private static readonly KdfParams TestKdf = new(1024, 1, 1);

    [Fact]
    public void CreateLockUnlockWithSamePasswordRestoresEntries()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-01-02T03:04:05+00:00"));
        var store = new MemoryVaultStore();
        var service = new VaultService(store, clock, TestKdf);
        service.Create(Password);
        service.Data.Entries.Add(CreateEntry());
        service.Save();
        var expected = VaultJson.Serialize(service.Data);

        Assert.All(service.Data.Categories, category => Assert.Equal(clock.UtcNow, category.UpdatedAt));

        service.Lock();
        service.Unlock(Password);

        Assert.Equal(expected, VaultJson.Serialize(service.Data));
    }

    [Fact]
    public void UnlockWithWrongPasswordThrowsAndStaysLocked()
    {
        var service = CreateService();
        service.Create(Password);
        service.Lock();

        Assert.Throws<WrongPasswordException>(() => service.Unlock("incorrect password"));

        Assert.True(service.IsLocked);
        Assert.Throws<InvalidOperationException>(() => service.Data);
    }

    [Fact]
    public void CreateWithShortPasswordThrowsArgumentException()
    {
        var store = new MemoryVaultStore();
        var service = new VaultService(store, new FakeClock(DateTimeOffset.UtcNow), TestKdf);

        Assert.Throws<ArgumentException>(() => service.Create("short"));

        Assert.False(store.Exists);
    }

    [Fact]
    public void LockRaisesLockedEventAndMakesDataUnavailable()
    {
        var service = CreateService();
        service.Create(Password);
        var lockedEvents = 0;
        service.Locked += (_, _) => lockedEvents++;

        service.Lock();

        Assert.Equal(1, lockedEvents);
        Assert.True(service.IsLocked);
        Assert.Throws<InvalidOperationException>(() => service.Data);
    }

    [Fact]
    public void ChangePasswordWithWrongCurrentPasswordChangesNothing()
    {
        var store = new MemoryVaultStore();
        var biometricUnlock = new FakeBiometricUnlock { IsEnabled = true };
        var service = new VaultService(
            store,
            new FakeClock(DateTimeOffset.UtcNow),
            TestKdf,
            biometricUnlock);
        service.Create(Password);
        service.Data.Entries.Add(CreateEntry());
        service.Save();
        var fileBefore = store.ReadAll();
        var dataBefore = VaultJson.Serialize(service.Data);

        Assert.Throws<WrongPasswordException>(
            () => service.ChangePassword("incorrect password", NewPassword));

        Assert.Equal(fileBefore, store.ReadAll());
        Assert.Equal(dataBefore, VaultJson.Serialize(service.Data));
        Assert.False(service.IsLocked);
        Assert.Equal(0, biometricUnlock.DisableCount);
    }

    [Fact]
    public void UnlockWithKeyRestoresVaultAndOwnsKey()
    {
        var service = CreateService();
        service.Create(Password);
        service.Data.Entries.Add(CreateEntry());
        service.Save();
        var key = service.VerifyPasswordAndGetKey(Password);
        service.Lock();

        service.UnlockWithKey(key);

        Assert.Single(service.Data.Entries);
        service.Lock();
    }

    [Fact]
    public void ChangePasswordDisablesBiometricUnlock()
    {
        var biometricUnlock = new FakeBiometricUnlock { IsEnabled = true };
        var service = new VaultService(
            new MemoryVaultStore(),
            new FakeClock(DateTimeOffset.UtcNow),
            TestKdf,
            biometricUnlock);
        service.Create(Password);

        service.ChangePassword(Password, NewPassword);

        Assert.False(biometricUnlock.IsEnabled);
        Assert.Equal(1, biometricUnlock.DisableCount);
    }

    [Fact]
    public void ChangePasswordPreventsUnlockWithOldPassword()
    {
        var service = CreateService();
        service.Create(Password);
        service.Data.Entries.Add(CreateEntry());
        service.Save();

        service.ChangePassword(Password, NewPassword);
        service.Lock();

        Assert.Throws<WrongPasswordException>(() => service.Unlock(Password));
        Assert.True(service.IsLocked);

        service.Unlock(NewPassword);
        Assert.Single(service.Data.Entries);
    }

    [Fact]
    public void VerifyPasswordDoesNotChangeUnlockedVault()
    {
        var service = CreateService();
        service.Create(Password);
        service.Data.Entries.Add(CreateEntry());
        var expectedData = VaultJson.Serialize(service.Data);

        Assert.Throws<WrongPasswordException>(() => service.VerifyPassword("incorrect password"));

        service.VerifyPassword(Password);

        Assert.False(service.IsLocked);
        Assert.Equal(expectedData, VaultJson.Serialize(service.Data));
    }

    [Fact]
    public void RestoreFromFileAdoptsFileAndItsKey()
    {
        var sourceStore = new MemoryVaultStore();
        var sourceService = new VaultService(sourceStore, new FakeClock(DateTimeOffset.UtcNow), TestKdf);
        sourceService.Create(Password);
        sourceService.Data.Entries.Add(CreateEntry());
        sourceService.Save();
        var sourceFile = sourceStore.ReadAll();
        var expectedData = VaultJson.Serialize(sourceService.Data);

        var destinationStore = new MemoryVaultStore();
        var destinationService = new VaultService(destinationStore, new FakeClock(DateTimeOffset.UtcNow), TestKdf);
        destinationService.Create(NewPassword);

        destinationService.RestoreFromFile(sourceFile, Password);

        Assert.Equal(sourceFile, destinationStore.ReadAll());
        Assert.Equal(expectedData, VaultJson.Serialize(destinationService.Data));
        destinationService.Lock();
        destinationService.Unlock(Password);
        Assert.Equal(expectedData, VaultJson.Serialize(destinationService.Data));
    }

    private static VaultService CreateService()
    {
        return new VaultService(
            new MemoryVaultStore(),
            new FakeClock(DateTimeOffset.Parse("2026-01-02T03:04:05+00:00")),
            TestKdf);
    }

    private static VaultEntry CreateEntry()
    {
        return new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = "حسابي",
            Username = "user",
            Password = "رمز 🔐",
            CreatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05+00:00"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05+00:00")
        };
    }

    private sealed class MemoryVaultStore : IVaultStore
    {
        private byte[]? _data;

        public bool Exists => _data is not null;

        public byte[] ReadAll()
        {
            return _data?.ToArray() ?? throw new FileNotFoundException("No vault is stored.");
        }

        public void WriteAll(byte[] data)
        {
            _data = data.ToArray();
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class FakeBiometricUnlock : IBiometricUnlock
    {
        public bool IsAvailable { get; set; } = true;
        public bool IsEnabled { get; set; }
        public int DisableCount { get; private set; }

        public Task EnableAsync(VaultKey key) => Task.CompletedTask;

        public Task<VaultKey?> UnlockAsync() => Task.FromResult<VaultKey?>(null);

        public void Disable()
        {
            IsEnabled = false;
            DisableCount++;
        }
    }
}
