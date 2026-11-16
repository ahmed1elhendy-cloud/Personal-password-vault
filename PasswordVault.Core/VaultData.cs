namespace PasswordVault.Core;

public sealed class VaultData
{
    public int SchemaVersion { get; set; } = 1;
    public List<VaultEntry> Entries { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public VaultSettings Settings { get; set; } = new();

    public static VaultData CreateNew()
    {
        return CreateNew(DateTimeOffset.UtcNow);
    }

    public static VaultData CreateNew(DateTimeOffset now)
    {
        return new VaultData
        {
            Categories =
            [
                CreateCategory("Email", "#E1F5EE", "#085041", "email", now),
                CreateCategory("Banking", "#FAEEDA", "#633806", "banking", now),
                CreateCategory("Shopping", "#FBEAF0", "#72243E", "shopping", now),
                CreateCategory("Social", "#EEEDFE", "#3C3489", "social", now),
                CreateCategory("Work", "#FAECE7", "#712B13", "work", now)
            ]
        };
    }

    private static Category CreateCategory(string name, string background, string text, string iconKey, DateTimeOffset now)
    {
        return new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            BackgroundHex = background,
            TextHex = text,
            IconKey = iconKey,
            UpdatedAt = now
        };
    }
}
