using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class BackupTests
{
    private const string VaultPassword = "current vault password";
    private const string BackupPassword = "backup file password";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-03-02T03:04:05+00:00");
    private static readonly KdfParams TestKdf = new(1024, 1, 1);

    [Fact]
    public void ExportFileBytesRestoresAllEntriesIntoFreshVaultService()
    {
        var sourceStore = new MemoryVaultStore();
        var source = CreateVault(sourceStore, VaultPassword);
        source.Data.Entries.Add(CreateEntry(Guid.NewGuid(), "One", "one-secret", 1));
        source.Data.Entries.Add(CreateEntry(Guid.NewGuid(), "Two", "two-secret", 2));
        source.Data.Settings.AutoLockSeconds = 90;
        source.Save();
        var expectedData = VaultJson.Serialize(source.Data);
        var backupFile = BackupService.ExportFileBytes(source);
        var freshStore = new MemoryVaultStore();
        var fresh = new VaultService(freshStore, new FakeClock(Now), TestKdf);

        fresh.RestoreFromFile(backupFile, VaultPassword);

        Assert.Equal(expectedData, VaultJson.Serialize(fresh.Data));
        Assert.Equal(backupFile, freshStore.ReadAll());
    }

    [Fact]
    public void ImportMergesBackupEntriesAndCountsAddedOrUpdatedEntries()
    {
        var sharedId = Guid.NewGuid();
        var localStore = new MemoryVaultStore();
        var local = CreateVault(localStore, VaultPassword);
        local.Data.Entries.Add(CreateEntry(sharedId, "Old version", "old-secret", 1));
        local.Data.Entries.Add(CreateEntry(Guid.NewGuid(), "Local only", "local-secret", 2));
        local.Save();

        var backupStore = new MemoryVaultStore();
        var backup = CreateVault(backupStore, BackupPassword);
        backup.Data.Entries.Add(CreateEntry(sharedId, "Updated version", "updated-secret", 3));
        backup.Data.Entries.Add(CreateEntry(Guid.NewGuid(), "Backup only", "backup-secret", 2));
        backup.Save();
        var backupFile = BackupService.ExportFileBytes(backup);

        var changedCount = BackupService.Import(local, backupFile, BackupPassword);

        Assert.Equal(2, changedCount);
        Assert.Equal(3, local.Data.Entries.Count);
        Assert.Contains(local.Data.Entries, entry => entry.Title == "Updated version");
        Assert.Contains(local.Data.Entries, entry => entry.Title == "Local only");
        Assert.Contains(local.Data.Entries, entry => entry.Title == "Backup only");

        local.Lock();
        local.Unlock(VaultPassword);
        Assert.Equal(3, local.Data.Entries.Count);
    }

    [Fact]
    public void ImportWithWrongBackupPasswordThrowsAndChangesNothing()
    {
        var backupStore = new MemoryVaultStore();
        var backup = CreateVault(backupStore, BackupPassword);
        backup.Data.Entries.Add(CreateEntry(Guid.NewGuid(), "Remote", "remote-secret", 2));
        backup.Save();
        var backupFile = BackupService.ExportFileBytes(backup);

        var localStore = new MemoryVaultStore();
        var local = CreateVault(localStore, VaultPassword);
        local.Data.Entries.Add(CreateEntry(Guid.NewGuid(), "Local", "local-secret", 1));
        local.Save();
        var fileBefore = localStore.ReadAll();
        var dataBefore = VaultJson.Serialize(local.Data);

        Assert.Throws<WrongPasswordException>(() =>
            BackupService.Import(local, backupFile, "incorrect backup password"));

        Assert.Equal(fileBefore, localStore.ReadAll());
        Assert.Equal(dataBefore, VaultJson.Serialize(local.Data));
        Assert.False(local.IsLocked);
    }

    private static VaultService CreateVault(MemoryVaultStore store, string password)
    {
        var service = new VaultService(store, new FakeClock(Now), TestKdf);
        service.Create(password);
        return service;
    }

    private static VaultEntry CreateEntry(Guid id, string title, string password, int minutes)
    {
        return new VaultEntry
        {
            Id = id,
            Title = title,
            Username = $"user-{title}",
            Password = password,
            UpdatedAt = Now.AddMinutes(minutes),
            CreatedAt = Now
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
}
