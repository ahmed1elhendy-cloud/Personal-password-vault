namespace PasswordVault.Core;

public sealed class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BackgroundHex { get; set; } = string.Empty;
    public string TextHex { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
