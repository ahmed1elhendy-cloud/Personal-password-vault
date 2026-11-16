using System.Globalization;
using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class SyncTests
{
    private const string Password = "sync vault password";
    private const string ChangedPassword = "changed vault password";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-03-05T06:07:08+00:00");
    private static readonly KdfParams TestKdf = new(1024, 1, 1);

    [Fact]
    public async Task SyncWithoutRemoteFileUploadsLocalVault()
    {
        var local = CreateVault(Password);
        var cloud = new MemoryCloudFileStore();

        var outcome = await SyncService.SyncAsync(
            local.Service,
            cloud,
            remotePassword: null,
            CancellationToken.None);

        Assert.Equal(SyncOutcome.Uploaded, outcome);
        Assert.NotNull(cloud.CurrentData);
        using var key = VaultCodec.OpenKey(Password, cloud.CurrentData!);
        Assert.Single(VaultCodec.Decrypt(cloud.CurrentData!, key).Entries);
    }

    [Fact]
    public async Task TwoVaultsConvergeAfterSyncingInTurn()
    {
        var first = CreateVault(Password);
        var second = RestoreVault(BackupService.ExportFileBytes(first.Service), Password);
        first.Service.Data.Entries.Add(CreateEntry("Phone edit", 2));
        first.Service.Save();
        second.Service.Data.Entries.Add(CreateEntry("PC edit", 3));
        second.Service.Save();
        var cloud = new MemoryCloudFileStore();

        Assert.Equal(
            SyncOutcome.Uploaded,
            await SyncService.SyncAsync(first.Service, cloud, null, CancellationToken.None));
        Assert.Equal(
            SyncOutcome.Merged,
            await SyncService.SyncAsync(second.Service, cloud, null, CancellationToken.None));
        Assert.Equal(
            SyncOutcome.Merged,
            await SyncService.SyncAsync(first.Service, cloud, null, CancellationToken.None));

        AssertEntryTitlesEqual(first.Service.Data, second.Service.Data);
        Assert.Contains(first.Service.Data.Entries, entry => entry.Title == "Phone edit");
        Assert.Contains(first.Service.Data.Entries, entry => entry.Title == "PC edit");
    }

    [Fact]
    public async Task ConflictingUploadRetriesAndPreservesBothChanges()
    {
        var local = CreateVault(Password);
        var initialFile = BackupService.ExportFileBytes(local.Service);
        var concurrent = RestoreVault(initialFile, Password);
        var cloud = new MemoryCloudFileStore();
        cloud.Seed(initialFile);
        local.Service.Data.Entries.Add(CreateEntry("Local change", 2));
        local.Service.Save();
        concurrent.Service.Data.Entries.Add(CreateEntry("Concurrent cloud change", 3));
        concurrent.Service.Save();
        cloud.BeforeNextUpload = () =>
            cloud.Seed(BackupService.ExportFileBytes(concurrent.Service));

        var outcome = await SyncService.SyncAsync(
            local.Service,
            cloud,
            remotePassword: null,
            CancellationToken.None);

        Assert.Equal(SyncOutcome.Merged, outcome);
        Assert.Equal(2, cloud.UploadAttempts);
        Assert.Contains(local.Service.Data.Entries, entry => entry.Title == "Local change");
        Assert.Contains(local.Service.Data.Entries, entry => entry.Title == "Concurrent cloud change");
        Assert.Contains(cloud.Decrypt(Password).Entries, entry => entry.Title == "Local change");
        Assert.Contains(cloud.Decrypt(Password).Entries, entry => entry.Title == "Concurrent cloud change");
    }

    [Fact]
    public async Task ChangedRemoteMasterPasswordRequiresPasswordThenIsAdopted()
    {
        var deviceA = CreateVault(Password);
        var deviceB = RestoreVault(BackupService.ExportFileBytes(deviceA.Service), Password);
        deviceA.Service.ChangePassword(Password, ChangedPassword);
        var cloud = new MemoryCloudFileStore();
        cloud.Seed(BackupService.ExportFileBytes(deviceA.Service));
        var localFileBefore = deviceB.Store.ReadAll();
        var localDataBefore = VaultJson.Serialize(deviceB.Service.Data);

        var needsPassword = await SyncService.SyncAsync(
            deviceB.Service,
            cloud,
            remotePassword: null,
            CancellationToken.None);

        Assert.Equal(SyncOutcome.NeedsPassword, needsPassword);
        Assert.Equal(localFileBefore, deviceB.Store.ReadAll());
        Assert.Equal(localDataBefore, VaultJson.Serialize(deviceB.Service.Data));
        Assert.Equal(0, cloud.UploadAttempts);

        var merged = await SyncService.SyncAsync(
            deviceB.Service,
            cloud,
            ChangedPassword,
            CancellationToken.None);

        Assert.Equal(SyncOutcome.Merged, merged);
        deviceB.Service.Lock();
        Assert.Throws<WrongPasswordException>(() => deviceB.Service.Unlock(Password));
        deviceB.Service.Unlock(ChangedPassword);
    }

    [Fact]
    public async Task WrongRemotePasswordThrowsWithoutChangingLocalVault()
    {
        var deviceA = CreateVault(Password);
        var deviceB = RestoreVault(BackupService.ExportFileBytes(deviceA.Service), Password);
        deviceA.Service.ChangePassword(Password, ChangedPassword);
        var cloud = new MemoryCloudFileStore();
        cloud.Seed(BackupService.ExportFileBytes(deviceA.Service));
        var localFileBefore = deviceB.Store.ReadAll();
        var localDataBefore = VaultJson.Serialize(deviceB.Service.Data);

        await Assert.ThrowsAsync<WrongPasswordException>(() => SyncService.SyncAsync(
            deviceB.Service,
            cloud,
            "incorrect remote password",
            CancellationToken.None));

        Assert.Equal(localFileBefore, deviceB.Store.ReadAll());
        Assert.Equal(localDataBefore, VaultJson.Serialize(deviceB.Service.Data));
        Assert.Equal(0, cloud.UploadAttempts);
    }

    [Fact]
    public async Task FailedAfterThreeConflictsLeavesLocalVaultUnchanged()
    {
        var local = CreateVault(Password);
        var remoteFile = BackupService.ExportFileBytes(local.Service);
        var cloud = new MemoryCloudFileStore { ForceConflicts = true };
        cloud.Seed(remoteFile);
        local.Service.Data.Entries.Add(CreateEntry("Unsynced local change", 2));
        local.Service.Save();
        var localFileBefore = local.Store.ReadAll();
        var localDataBefore = VaultJson.Serialize(local.Service.Data);

        var outcome = await SyncService.SyncAsync(
            local.Service,
            cloud,
            remotePassword: null,
            CancellationToken.None);

        Assert.Equal(SyncOutcome.Failed, outcome);
        Assert.Equal(3, cloud.UploadAttempts);
        Assert.Equal(localFileBefore, local.Store.ReadAll());
        Assert.Equal(localDataBefore, VaultJson.Serialize(local.Service.Data));
    }

    private static TestVault CreateVault(string password)
    {
        var store = new MemoryVaultStore();
        var service = new VaultService(store, new FakeClock(Now), TestKdf);
        service.Create(password);
        service.Data.Entries.Add(CreateEntry("Base entry", 1));
        service.Save();
        return new TestVault(service, store);
    }

    private static TestVault RestoreVault(byte[] file, string password)
    {
        var store = new MemoryVaultStore();
        var service = new VaultService(store, new FakeClock(Now), TestKdf);
        service.RestoreFromFile(file, password);
        return new TestVault(service, store);
    }

    private static VaultEntry CreateEntry(string title, int minutes)
    {
        return new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = title,
            Username = $"user-{title}",
            Password = $"password-{title}",
            CreatedAt = Now,
            UpdatedAt = Now.AddMinutes(minutes)
        };
    }

    private static void AssertEntryTitlesEqual(VaultData first, VaultData second)
    {
        Assert.Equal(
            first.Entries.Select(entry => entry.Title).OrderBy(title => title, StringComparer.Ordinal),
            second.Entries.Select(entry => entry.Title).OrderBy(title => title, StringComparer.Ordinal));
    }

    private sealed record TestVault(VaultService Service, MemoryVaultStore Store);

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

    private sealed class MemoryCloudFileStore : ICloudFileStore
    {
        private byte[]? _data;
        private int _version;

        public int UploadAttempts { get; private set; }
        public bool ForceConflicts { get; init; }
        public Action? BeforeNextUpload { get; set; }
        public byte[]? CurrentData => _data?.ToArray();

        public Task<RemoteFile?> DownloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_data is null
                ? null
                : new RemoteFile(_data.ToArray(), _version.ToString(CultureInfo.InvariantCulture)));
        }

        public Task<bool> UploadAsync(
            byte[] data,
            string? expectedVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UploadAttempts++;
            var beforeUpload = BeforeNextUpload;
            BeforeNextUpload = null;
            beforeUpload?.Invoke();

            if (ForceConflicts)
            {
                _version++;
                return Task.FromResult(false);
            }

            var currentVersion = _data is null
                ? null
                : _version.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _data = data.ToArray();
            _version++;
            return Task.FromResult(true);
        }

        public void Seed(byte[] data)
        {
            _data = data.ToArray();
            _version++;
        }

        public VaultData Decrypt(string password)
        {
            var file = _data ?? throw new InvalidOperationException("No cloud file is stored.");
            using var key = VaultCodec.OpenKey(password, file);
            return VaultCodec.Decrypt(file, key);
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
