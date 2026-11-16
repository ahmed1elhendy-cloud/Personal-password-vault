namespace PasswordVault.Core;

public static class AutoLockPolicy
{
    public static bool ShouldLock(TimeSpan elapsed, int autoLockSeconds)
    {
        return autoLockSeconds <= 0 || elapsed >= TimeSpan.FromSeconds(autoLockSeconds);
    }
}
