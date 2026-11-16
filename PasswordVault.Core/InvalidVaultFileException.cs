namespace PasswordVault.Core;

public sealed class InvalidVaultFileException : IOException
{
    public InvalidVaultFileException(string message)
        : base(message)
    {
    }

    public InvalidVaultFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
