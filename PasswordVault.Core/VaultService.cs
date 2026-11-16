using System.Security.Cryptography;

namespace PasswordVault.Core;

public sealed class VaultService
{
    private readonly IVaultStore _store;
    private readonly IClock _clock;
    private readonly KdfParams? _createKdfParams;
    private VaultData? _data;
    private VaultKey? _key;

    public VaultService(IVaultStore store, IClock clock)
        : this(store, clock, createKdfParams: null)
    {
    }

    internal VaultService(IVaultStore store, IClock clock, KdfParams? createKdfParams)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
        _createKdfParams = createKdfParams;
    }

    public event EventHandler? Locked;
    public event EventHandler? Unlocked;

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

    public void Save()
    {
        var key = _key ?? throw new InvalidOperationException("The vault is locked.");
        _store.WriteAll(VaultCodec.Encrypt(Data, key));
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
            _store.WriteAll(VaultCodec.Encrypt(Data, newKey));
            var oldKey = _key!;
            _key = newKey;
            newKey = null;
            oldKey.Dispose();
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
            _store.WriteAll(file);
            var restoredKey = key;
            key = null;
            SetUnlocked(data, restoredKey);
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

    private static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < 10)
        {
            throw new ArgumentException("The vault password must be at least 10 characters long.", nameof(password));
        }
    }
}
