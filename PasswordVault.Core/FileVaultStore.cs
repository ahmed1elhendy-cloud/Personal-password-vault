namespace PasswordVault.Core;

public sealed class FileVaultStore : IVaultStore
{
    private readonly string _path;
    private readonly string _temporaryPath;
    private readonly string _backupPath;
    private readonly Action<Stream, byte[]> _writeTemporaryFile;

    public FileVaultStore(string path)
        : this(path, static (stream, data) => stream.Write(data, 0, data.Length))
    {
    }

    internal FileVaultStore(string path, Action<Stream, byte[]> writeTemporaryFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeTemporaryFile);

        _path = Path.GetFullPath(path);
        _temporaryPath = _path + ".tmp";
        _backupPath = _path + ".bak";
        _writeTemporaryFile = writeTemporaryFile;
    }

    public bool Exists => File.Exists(_path);

    public byte[] ReadAll()
    {
        return File.ReadAllBytes(_path);
    }

    public void WriteAll(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The vault path must include a directory.");
        Directory.CreateDirectory(directory);

        using (var stream = new FileStream(
            _temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        {
            _writeTemporaryFile(stream, data);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(_path))
        {
            File.Replace(_temporaryPath, _path, _backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(_temporaryPath, _path);
        }
    }
}
