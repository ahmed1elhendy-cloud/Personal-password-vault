namespace PasswordVault.Core;

public sealed class VaultEntry
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public List<CustomField> CustomFields { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
