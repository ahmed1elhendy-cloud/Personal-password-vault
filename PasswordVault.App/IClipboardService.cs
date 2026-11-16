namespace PasswordVault.App;

public interface IClipboardService
{
    Task CopySecret(string text, int clearAfterSeconds);
}
