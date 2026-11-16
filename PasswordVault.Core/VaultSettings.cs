namespace PasswordVault.Core;

public sealed class VaultSettings
{
    public int AutoLockSeconds { get; set; } = 60;
    public int ClipboardClearSeconds { get; set; } = 30;
}
