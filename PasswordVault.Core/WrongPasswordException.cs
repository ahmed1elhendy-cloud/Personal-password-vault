using System.Security.Cryptography;

namespace PasswordVault.Core;

public sealed class WrongPasswordException : CryptographicException
{
    public WrongPasswordException()
        : base("The password is incorrect or the vault contents have been modified.")
    {
    }

    public WrongPasswordException(Exception innerException)
        : base("The password is incorrect or the vault contents have been modified.", innerException)
    {
    }
}
