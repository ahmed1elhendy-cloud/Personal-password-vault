namespace PasswordVault.Core;

public interface IVaultStore
{
    bool Exists { get; }
    byte[] ReadAll();
    void WriteAll(byte[] data);
}
