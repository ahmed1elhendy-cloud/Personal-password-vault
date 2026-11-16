using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class FileVaultStoreTests
{
    [Fact]
    public void WriteAllThenReadAllReturnsBytes()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "vault.pvlt");
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var store = new FileVaultStore(path);

        store.WriteAll(data);

        Assert.Equal(data, store.ReadAll());
    }

    [Fact]
    public void WriteFailureLeavesPreviousVaultUnchangedAndSuccessfulReplaceKeepsBackup()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "vault.pvlt");
        var previousVault = new byte[] { 1, 2, 3, 4 };
        var replacementVault = new byte[] { 5, 6, 7, 8 };
        var store = new FileVaultStore(path);
        store.WriteAll(previousVault);

        var failingStore = new FileVaultStore(path, (stream, data) =>
        {
            stream.Write(data, 0, data.Length / 2);
            throw new IOException("Simulated partial write failure.");
        });

        Assert.Throws<IOException>(() => failingStore.WriteAll(replacementVault));
        Assert.Equal(previousVault, store.ReadAll());

        store.WriteAll(replacementVault);

        Assert.Equal(replacementVault, store.ReadAll());
        Assert.Equal(previousVault, File.ReadAllBytes(path + ".bak"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
