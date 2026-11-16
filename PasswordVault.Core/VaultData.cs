namespace PasswordVault.Core;

public sealed class VaultData
{
    public int SchemaVersion { get; set; } = 1;
    public List<VaultEntry> Entries { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public VaultSettings Settings { get; set; } = new();

    public static VaultData CreateNew()
    {
        return new VaultData
        {
            Categories =
            [
                CreateCategory("Email", "#E1F5EE", "#085041", "email"),
                CreateCategory("Banking", "#FAEEDA", "#633806", "banking"),
                CreateCategory("Shopping", "#FBEAF0", "#72243E", "shopping"),
                CreateCategory("Social", "#EEEDFE", "#3C3489", "social"),
                CreateCategory("Work", "#FAECE7", "#712B13", "work")
            ]
        };
    }

    private static Category CreateCategory(string name, string background, string text, string iconKey)
    {
        return new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            BackgroundHex = background,
            TextHex = text,
            IconKey = iconKey,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
