using System.Security.Cryptography;
using Android.Security.Keystore;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Java.Lang;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using PasswordVault.Core;
using AndroidCipherMode = Javax.Crypto.CipherMode;

namespace PasswordVault.App;

public sealed class AndroidBiometricUnlock : IBiometricUnlock
{
    private const string KeyAlias = "password-vault-biometric-key";
    private const string PreferencePrefix = "biometric.unlock.";
    private const string CiphertextKey = PreferencePrefix + "ciphertext";
    private const string IvKey = PreferencePrefix + "iv";
    private const string SaltKey = PreferencePrefix + "salt";
    private const string MemoryKey = PreferencePrefix + "memory-kb";
    private const string IterationsKey = PreferencePrefix + "iterations";
    private const string ParallelismKey = PreferencePrefix + "parallelism";
    private readonly BiometricPolicy _policy;

    public AndroidBiometricUnlock(BiometricPolicy policy)
    {
        _policy = policy;
    }

    public bool IsAvailable
    {
        get
        {
            var context = Android.App.Application.Context;
            return BiometricManager.From(context).CanAuthenticate(
                BiometricManager.Authenticators.BiometricStrong) == BiometricManager.BiometricSuccess;
        }
    }

    public bool IsEnabled
    {
        get
        {
            if (!TryReadStoredValues(out _, out var iv, out _, out _))
            {
                return false;
            }

            try
            {
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
                    ?? throw new CryptographicException("Could not initialize fingerprint encryption.");
                cipher.Init(AndroidCipherMode.DecryptMode, GetSecretKey(), new GCMParameterSpec(128, iv));
                return true;
            }
            catch (KeyPermanentlyInvalidatedException)
            {
                Disable();
                return false;
            }
            catch (InvalidOperationException)
            {
                Disable();
                return false;
            }
        }
    }

    public async Task EnableAsync(VaultKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Fingerprint authentication is unavailable.");
        }

        Disable();
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new CryptographicException("Could not initialize fingerprint encryption.");
        cipher.Init(AndroidCipherMode.EncryptMode, CreateSecretKey());
        var keyBytes = key.ExportKeyBytes();
        try
        {
            var result = await AuthenticateAsync(cipher, "Enable fingerprint unlock");
            if (result?.CryptoObject?.Cipher is not { } authenticatedCipher)
            {
                throw new System.OperationCanceledException("Fingerprint setup was canceled.");
            }

            var ciphertext = authenticatedCipher.DoFinal(keyBytes)
                ?? throw new CryptographicException("Could not encrypt the fingerprint key.");
            var salt = key.Salt;
            var storedIv = authenticatedCipher.GetIV()
                ?? throw new CryptographicException("Could not store fingerprint encryption data.");
            var preferences = Preferences.Default;
            preferences.Set(CiphertextKey, Convert.ToBase64String(ciphertext));
            preferences.Set(IvKey, Convert.ToBase64String(storedIv));
            preferences.Set(SaltKey, Convert.ToBase64String(salt));
            preferences.Set(MemoryKey, key.Kdf.MemoryKb);
            preferences.Set(IterationsKey, key.Kdf.Iterations);
            preferences.Set(ParallelismKey, key.Kdf.Parallelism);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public async Task<VaultKey?> UnlockAsync()
    {
        if (!IsEnabled
            || !TryReadStoredValues(out var ciphertext, out var iv, out var salt, out var kdf))
        {
            return null;
        }

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new CryptographicException("Could not initialize fingerprint encryption.");
        try
        {
            cipher.Init(AndroidCipherMode.DecryptMode, GetSecretKey(), new GCMParameterSpec(128, iv));
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            Disable();
            return null;
        }

        var result = await AuthenticateAsync(cipher, "Unlock your vault");
        if (result?.CryptoObject?.Cipher is not { } authenticatedCipher)
        {
            return null;
        }

        byte[]? keyBytes = null;
        try
        {
            keyBytes = authenticatedCipher.DoFinal(ciphertext)
                ?? throw new CryptographicException("Could not decrypt the fingerprint key.");
            return VaultKey.FromKeyBytes(keyBytes, salt, kdf);
        }
        catch (Javax.Crypto.AEADBadTagException)
        {
            Disable();
            return null;
        }
        finally
        {
            if (keyBytes is not null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
    }

    public void Disable()
    {
        var preferences = Preferences.Default;
        preferences.Remove(CiphertextKey);
        preferences.Remove(IvKey);
        preferences.Remove(SaltKey);
        preferences.Remove(MemoryKey);
        preferences.Remove(IterationsKey);
        preferences.Remove(ParallelismKey);

        using var keyStore = GetKeyStore();
        if (keyStore.ContainsAlias(KeyAlias))
        {
            keyStore.DeleteEntry(KeyAlias);
        }
    }

    private async Task<BiometricPrompt.AuthenticationResult?> AuthenticateAsync(
        Cipher cipher,
        string title)
    {
        var activity = Platform.CurrentActivity as FragmentActivity
            ?? throw new InvalidOperationException("Fingerprint authentication is unavailable.");
        var completion = new TaskCompletionSource<BiometricPrompt.AuthenticationResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        BiometricPrompt? prompt = null;
        var callback = new PromptCallback(
            result => completion.TrySetResult(result),
            () => completion.TrySetResult(null),
            () =>
            {
                if (_policy.RecordFingerprintFailure())
                {
                    prompt?.CancelAuthentication();
                }
            });
        var executor = ContextCompat.GetMainExecutor(activity)
            ?? throw new InvalidOperationException("Fingerprint authentication is unavailable.");
        prompt = new BiometricPrompt(activity, executor, callback);
        var promptInfo = new BiometricPrompt.PromptInfo.Builder()
            .SetTitle(title)
            .SetSubtitle("Authenticate with your fingerprint")
            .SetNegativeButtonText("Use password")
            .SetAllowedAuthenticators(BiometricManager.Authenticators.BiometricStrong)
            .Build();
        prompt.Authenticate(promptInfo, new BiometricPrompt.CryptoObject(cipher));
        return await completion.Task;
    }

    private static ISecretKey CreateSecretKey()
    {
        using var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")
            ?? throw new CryptographicException("Could not create the fingerprint key.");
        var keySpecBuilder = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetUserAuthenticationRequired(true)
            .SetInvalidatedByBiometricEnrollment(true);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            keySpecBuilder.SetUserAuthenticationParameters(
                0,
                (int)KeyPropertiesAuthType.BiometricStrong);
        }
        else
        {
            keySpecBuilder.SetUserAuthenticationValidityDurationSeconds(-1);
        }

        var keySpec = keySpecBuilder.Build();
        keyGenerator.Init(keySpec);
        return keyGenerator.GenerateKey()
            ?? throw new CryptographicException("Could not create the fingerprint key.");
    }

    private static ISecretKey GetSecretKey()
    {
        using var keyStore = GetKeyStore();
        return keyStore.GetKey(KeyAlias, null) as ISecretKey
            ?? throw new InvalidOperationException("The fingerprint key is unavailable.");
    }

    private static KeyStore GetKeyStore()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")
            ?? throw new InvalidOperationException("The Android Keystore is unavailable.");
        keyStore.Load((KeyStore.ILoadStoreParameter?)null);
        return keyStore;
    }

    private bool TryReadStoredValues(
        out byte[] ciphertext,
        out byte[] iv,
        out byte[] salt,
        out KdfParams kdf)
    {
        var preferences = Preferences.Default;
        ciphertext = [];
        iv = [];
        salt = [];
        kdf = KdfParams.Default;
        try
        {
            var ciphertextValue = preferences.Get(CiphertextKey, string.Empty);
            var ivValue = preferences.Get(IvKey, string.Empty);
            var saltValue = preferences.Get(SaltKey, string.Empty);
            if (string.IsNullOrEmpty(ciphertextValue)
                || string.IsNullOrEmpty(ivValue)
                || string.IsNullOrEmpty(saltValue))
            {
                Disable();
                return false;
            }

            ciphertext = Convert.FromBase64String(ciphertextValue);
            iv = Convert.FromBase64String(ivValue);
            salt = Convert.FromBase64String(saltValue);
            kdf = new KdfParams(
                preferences.Get(MemoryKey, 0),
                preferences.Get(IterationsKey, 0),
                preferences.Get(ParallelismKey, 0));
            return true;
        }
        catch (FormatException)
        {
            Disable();
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            Disable();
            return false;
        }
    }

    private sealed class PromptCallback(
        Action<BiometricPrompt.AuthenticationResult> onSuccess,
        Action onError,
        Action onFailure) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
        {
            onSuccess(result);
        }

        public override void OnAuthenticationError(int errorCode, ICharSequence? errString)
        {
            onError();
        }

        public override void OnAuthenticationFailed()
        {
            onFailure();
        }
    }
}
