using System.Security.Cryptography;

namespace PasswordVault.Core;

public static class PasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?/";

    public static string Generate(
        int length,
        bool includeLowercase,
        bool includeUppercase,
        bool includeDigits,
        bool includeSymbols)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 8);

        var characterClasses = new List<string>(4);
        if (includeLowercase)
        {
            characterClasses.Add(Lowercase);
        }

        if (includeUppercase)
        {
            characterClasses.Add(Uppercase);
        }

        if (includeDigits)
        {
            characterClasses.Add(Digits);
        }

        if (includeSymbols)
        {
            characterClasses.Add(Symbols);
        }

        if (characterClasses.Count == 0)
        {
            throw new ArgumentException("Select at least one character class.");
        }

        var allCharacters = string.Concat(characterClasses);
        var password = new char[length];
        var index = 0;

        foreach (var characterClass in characterClasses)
        {
            password[index++] = characterClass[RandomNumberGenerator.GetInt32(characterClass.Length)];
        }

        while (index < password.Length)
        {
            password[index++] = allCharacters[RandomNumberGenerator.GetInt32(allCharacters.Length)];
        }

        for (var i = password.Length - 1; i > 0; i--)
        {
            var otherIndex = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[otherIndex]) = (password[otherIndex], password[i]);
        }

        return new string(password);
    }
}
