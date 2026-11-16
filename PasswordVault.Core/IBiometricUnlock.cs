namespace PasswordVault.Core;

public interface IBiometricUnlock
{
    bool IsAvailable { get; }
    bool IsEnabled { get; }
    Task EnableAsync(VaultKey key);
    Task<VaultKey?> UnlockAsync();
    void Disable();
}
