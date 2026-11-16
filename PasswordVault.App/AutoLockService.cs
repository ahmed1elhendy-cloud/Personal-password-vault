using PasswordVault.Core;

namespace PasswordVault.App;

public sealed class AutoLockService(VaultService vaultService, IClock clock)
{
    private DateTimeOffset? _backgroundedAt;

    public void OnBackgrounded()
    {
        _backgroundedAt = clock.UtcNow;
    }

    public void OnResumed()
    {
        if (_backgroundedAt is not DateTimeOffset backgroundedAt)
        {
            return;
        }

        _backgroundedAt = null;
        if (vaultService.IsLocked)
        {
            return;
        }

        var elapsed = clock.UtcNow - backgroundedAt;
        if (AutoLockPolicy.ShouldLock(elapsed, vaultService.Data.Settings.AutoLockSeconds))
        {
            vaultService.Lock();
        }
    }
}
