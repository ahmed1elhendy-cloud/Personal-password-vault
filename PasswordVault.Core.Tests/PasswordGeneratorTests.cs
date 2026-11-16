using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void GenerateIncludesEverySelectedCharacterClass()
    {
        var password = PasswordGenerator.Generate(20, true, true, true, true);

        Assert.Equal(20, password.Length);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, character => !char.IsLetterOrDigit(character));
    }

    [Fact]
    public void GenerateWithOnlyDigitsReturnsOnlyDigits()
    {
        var password = PasswordGenerator.Generate(20, false, false, true, false);

        Assert.Equal(20, password.Length);
        Assert.All(password, character => Assert.InRange(character, '0', '9'));
    }

    [Fact]
    public void GenerateReturnsDifferentValuesAcrossCalls()
    {
        var first = PasswordGenerator.Generate(20, true, true, true, true);
        var second = PasswordGenerator.Generate(20, true, true, true, true);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateRejectsLengthsBelowEight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PasswordGenerator.Generate(7, true, true, true, true));
    }
}
