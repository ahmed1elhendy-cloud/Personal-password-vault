using System.Security.Cryptography;

namespace PasswordVault.Core;

public sealed class VaultService
{
    private readonly IVaultStore _store;
    private readonly IClock _clock;
    private readonly KdfParams? _createKdfParams;
    private readonly IBiometricUnlock? _biometricUnlock;
    private VaultData? _data;
    private VaultKey? _key;

    public VaultService(IVaultStore store, IClock clock)
        : this(store, clock, createKdfParams: null, biometricUnlock: null)
    {
    }

    public VaultService(IVaultStore store, IClock clock, IBiometricUnlock? biometricUnlock)
        : this(store, clock, createKdfParams: null, biometricUnlock: biometricUnlock)
    {
    }

    internal VaultService(IVaultStore store, IClock clock, KdfParams? createKdfParams)
        : this(store, clock, createKdfParams, biometricUnlock: null)
    {
    }

    internal VaultService(
        IVaultStore store,
        IClock clock,
        KdfParams? createKdfParams,
        IBiometricUnlock? biometricUnlock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
        _createKdfParams = createKdfParams;
        _biometricUnlock = biometricUnlock;
    }

    public event EventHandler? Locked;
    public event EventHandler? Unlocked;
    public event EventHandler? Saved;

    public bool HasVault => _store.Exists;
    public bool IsLocked => _key is null || _data is null;

    public VaultData Data => _data
        ?? throw new InvalidOperationException("The vault is locked.");

    public void Create(string password)
    {
        ValidatePassword(password);
        if (!IsLocked)
        {
            throw new InvalidOperationException("The vault is already unlocked.");
        }

        if (_store.Exists)
        {
            throw new InvalidOperationException("A vault already exists.");
        }

        var data = VaultData.CreateNew(_clock.UtcNow);
        VaultKey? key = CreateKey(password);
        try
        {
            _store.WriteAll(VaultCodec.Encrypt(data, key));
            var createdKey = key;
            key = null;
            SetUnlocked(data, createdKey);
        }
        finally
        {
            key?.Dispose();
        }
    }

    public void Unlock(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!IsLocked)
        {
            throw new InvalidOperationException("The vault is already unlocked.");
        }

        var file = _store.ReadAll();
        VaultKey? key = VaultCodec.OpenKey(password, file);
        try
        {
            var data = VaultCodec.Decrypt(file, key);
            var openedKey = key;
            key = null;
            SetUnlocked(data, openedKey);
        }
        finally
        {
            key?.Dispose();
        }
    }

    public void UnlockWithKey(VaultKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        VaultKey? pendingKey = key;
        try
        {
            if (!IsLocked)
            {
                throw new InvalidOperationException("The vault is already unlocked.");
            }

            var file = _store.ReadAll();
            var data = VaultCodec.Decrypt(file, pendingKey);
            SetUnlocked(data, pendingKey);
            pendingKey = null;
        }
        finally
        {
            pendingKey?.Dispose();
        }
    }

    public void Save()
    {
        var key = _key ?? throw new InvalidOperationException("The vault is locked.");
        _store.WriteAll(VaultCodec.Encrypt(Data, key));
        Saved?.Invoke(this, EventArgs.Empty);
    }

    public void VerifyPassword(string password)
    {
        using var key = VerifyPasswordAndGetKey(password);
    }

    public VaultKey VerifyPasswordAndGetKey(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        EnsureUnlocked();
        var file = _store.ReadAll();
        VaultKey? key = VaultCodec.OpenKey(password, file);
        try
        {
            _ = VaultCodec.Decrypt(file, key);
            var verifiedKey = key;
            key = null;
            return verifiedKey;
        }
        finally
        {
            key?.Dispose();
        }
    }

    internal byte[] ExportFileBytes()
    {
        var key = _key ?? throw new InvalidOperationException("The vault is locked.");
        return VaultCodec.Encrypt(Data, key);
    }

    internal void ReplaceDataAndSave(VaultData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var key = _key ?? throw new InvalidOperationException("The vault is locked.");
        _store.WriteAll(VaultCodec.Encrypt(data, key));
        _data = data;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    internal bool HasSameKeyParameters(ReadOnlySpan<byte> file)
    {
        var key = _key ?? throw new InvalidOperationException("The vault is locked.");
        return VaultCodec.SameKeyParameters(key, file);
    }

    internal VaultData DecryptWithCurrentKey(ReadOnlySpan<byte> file)
    {
        var key = _key ?? throw new InvalidOperationException("The vault is locked.");
        return VaultCodec.Decrypt(file, key);
    }

    internal byte[] EncryptWithCurrentKey(VaultData data)
    {
        var key = _key ?? throw new InvalidOperationException("The vault is locked.");
        return VaultCodec.Encrypt(data, key);
    }

    internal void CommitSyncedState(VaultData data, byte[] encryptedFile, VaultKey? adoptedKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(encryptedFile);
        var currentKey = _key ?? throw new InvalidOperationException("The vault is locked.");

        if (adoptedKey is not null)
        {
            DisableBiometricUnlock();
        }

        _store.WriteAll(encryptedFile);
        _data = data;
        if (adoptedKey is not null)
        {
            _key = adoptedKey;
            currentKey.Dispose();
        }
    }

    public void Lock()
    {
        if (IsLocked)
        {
            return;
        }

        _key?.Dispose();
        _key = null;
        _data = null;
        Locked?.Invoke(this, EventArgs.Empty);
    }

    public void ChangePassword(string currentPassword, string newPassword)
    {
        EnsureUnlocked();
        ValidatePassword(newPassword);

        var currentFile = _store.ReadAll();
        using (var currentKey = VaultCodec.OpenKey(currentPassword, currentFile))
        {
            _ = VaultCodec.Decrypt(currentFile, currentKey);
        }

        VaultKey? newKey = CreateKey(newPassword);
        try
        {
            DisableBiometricUnlock();
            _store.WriteAll(VaultCodec.Encrypt(Data, newKey));
            var oldKey = _key!;
            _key = newKey;
            newKey = null;
            oldKey.Dispose();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            newKey?.Dispose();
        }
    }

    public void RestoreFromFile(byte[] file, string password)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(password);

        VaultKey? key = VaultCodec.OpenKey(password, file);
        try
        {
            var data = VaultCodec.Decrypt(file, key);
            DisableBiometricUnlock();
            _store.WriteAll(file);
            var restoredKey = key;
            key = null;
            SetUnlocked(data, restoredKey);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            key?.Dispose();
        }
    }

    private VaultKey CreateKey(string password)
    {
        return _createKdfParams is null
            ? VaultCodec.CreateKey(password)
            : VaultCodec.CreateKey(password, _createKdfParams);
    }

    private void SetUnlocked(VaultData data, VaultKey key)
    {
        var wasLocked = IsLocked;
        var oldKey = _key;
        _data = data;
        _key = key;
        oldKey?.Dispose();
        if (wasLocked)
        {
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EnsureUnlocked()
    {
        if (IsLocked)
        {
            throw new InvalidOperationException("The vault is locked.");
        }
    }

    private void DisableBiometricUnlock()
    {
        if (_biometricUnlock?.IsEnabled == true)
        {
            _biometricUnlock.Disable();
        }
    }

    private static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < 10)
        {
            throw new ArgumentException("The vault password must be at least 10 characters long.", nameof(password));
        }
    }
}
