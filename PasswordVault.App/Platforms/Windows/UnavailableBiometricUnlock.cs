using PasswordVault.Core;

namespace PasswordVault.App;

public sealed class UnavailableBiometricUnlock : IBiometricUnlock
{
    public bool IsAvailable => false;
    public bool IsEnabled => false;

    public Task EnableAsync(VaultKey key)
    {
        throw new PlatformNotSupportedException("Biometric unlock is unavailable on this platform.");
    }

    public Task<VaultKey?> UnlockAsync() => Task.FromResult<VaultKey?>(null);

    public void Disable() { }
}
