namespace PasswordVault.Core;

public sealed class BiometricPolicy
{
    public const int MaximumFingerprintAttempts = 5;

    private bool _masterPasswordEntered;

    public int FailedFingerprintAttempts { get; private set; }

    public bool CanOfferFingerprint(bool isAvailable, bool isEnabled)
    {
        return isAvailable
            && isEnabled
            && _masterPasswordEntered
            && FailedFingerprintAttempts < MaximumFingerprintAttempts;
    }

    public void RecordMasterPasswordEntry()
    {
        _masterPasswordEntered = true;
    }

    public void RecordPasswordUnlock()
    {
        RecordMasterPasswordEntry();
        FailedFingerprintAttempts = 0;
    }

    public bool RecordFingerprintFailure()
    {
        if (FailedFingerprintAttempts < MaximumFingerprintAttempts)
        {
            FailedFingerprintAttempts++;
        }

        return FailedFingerprintAttempts >= MaximumFingerprintAttempts;
    }
}
