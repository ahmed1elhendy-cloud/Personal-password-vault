using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class AutoLockPolicyTests
{
    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void ShouldLockAtConfiguredThreshold(int elapsedSeconds, bool expected)
    {
        var shouldLock = AutoLockPolicy.ShouldLock(TimeSpan.FromSeconds(elapsedSeconds), 60);

        Assert.Equal(expected, shouldLock);
    }
}
