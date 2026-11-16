using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PasswordVault.Core;

public static class VaultJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static byte[] Serialize(VaultData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return StrictUtf8.GetBytes(JsonSerializer.Serialize(data, Options));
    }

    public static VaultData Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var json = StrictUtf8.GetString(data);
        return JsonSerializer.Deserialize<VaultData>(json, Options)
            ?? throw new JsonException("The vault JSON did not contain a vault data object.");
    }
}
