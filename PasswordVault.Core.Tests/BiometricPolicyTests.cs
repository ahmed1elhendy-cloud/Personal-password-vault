using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class BiometricPolicyTests
{
    [Fact]
    public void FingerprintIsOfferedOnlyWhenAvailableEnabledAndPasswordWasEntered()
    {
        var policy = new BiometricPolicy();

        Assert.False(policy.CanOfferFingerprint(isAvailable: true, isEnabled: true));

        policy.RecordMasterPasswordEntry();

        Assert.False(policy.CanOfferFingerprint(isAvailable: false, isEnabled: true));
        Assert.False(policy.CanOfferFingerprint(isAvailable: true, isEnabled: false));
        Assert.True(policy.CanOfferFingerprint(isAvailable: true, isEnabled: true));
    }

    [Fact]
    public void FiveFailedFingerprintAttemptsRequirePassword()
    {
        var policy = new BiometricPolicy();
        policy.RecordMasterPasswordEntry();

        for (var attempt = 1; attempt < BiometricPolicy.MaximumFingerprintAttempts; attempt++)
        {
            Assert.False(policy.RecordFingerprintFailure());
        }

        Assert.True(policy.RecordFingerprintFailure());
        Assert.Equal(BiometricPolicy.MaximumFingerprintAttempts, policy.FailedFingerprintAttempts);
        Assert.False(policy.CanOfferFingerprint(isAvailable: true, isEnabled: true));
        Assert.True(policy.RecordFingerprintFailure());
    }

    [Fact]
    public void SuccessfulPasswordUnlockResetsFingerprintFailures()
    {
        var policy = new BiometricPolicy();
        policy.RecordMasterPasswordEntry();
        for (var attempt = 0; attempt < BiometricPolicy.MaximumFingerprintAttempts; attempt++)
        {
            policy.RecordFingerprintFailure();
        }

        policy.RecordPasswordUnlock();

        Assert.Equal(0, policy.FailedFingerprintAttempts);
        Assert.True(policy.CanOfferFingerprint(isAvailable: true, isEnabled: true));
    }
}
