using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class VaultDataTests
{
    [Fact]
    public void VaultData_CreateNew_HasFiveDefaultCategories()
    {
        var data = VaultData.CreateNew();

        Assert.Collection(
            data.Categories,
            category => AssertCategory(category, "Email", "#E1F5EE", "#085041"),
            category => AssertCategory(category, "Banking", "#FAEEDA", "#633806"),
            category => AssertCategory(category, "Shopping", "#FBEAF0", "#72243E"),
            category => AssertCategory(category, "Social", "#EEEDFE", "#3C3489"),
            category => AssertCategory(category, "Work", "#FAECE7", "#712B13"));
    }

    [Fact]
    public void VaultJson_RoundTrip_KeepsArabicAndEmoji()
    {
        var entry = new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = "حسابي",
            Notes = "ملاحظة 🔐",
            Website = "https://example.com",
            Username = "user",
            CategoryId = Guid.NewGuid(),
            CustomFields = [new CustomField { Name = "مفتاح", Value = "قيمة 🔑", IsHidden = true }],
            CreatedAt = DateTimeOffset.Parse("2025-03-04T05:06:07+00:00"),
            UpdatedAt = DateTimeOffset.Parse("2025-03-05T05:06:07+00:00")
        };
        var original = VaultData.CreateNew();
        original.Entries.Add(entry);

        var serialized = VaultJson.Serialize(original);
        var deserialized = VaultJson.Deserialize(serialized);

        Assert.Equal(serialized, VaultJson.Serialize(deserialized));
        Assert.Equal(entry.Title, deserialized.Entries[0].Title);
        Assert.Equal(entry.Notes, deserialized.Entries[0].Notes);
    }

    private static void AssertCategory(Category category, string name, string background, string text)
    {
        Assert.Equal(name, category.Name);
        Assert.Equal(background, category.BackgroundHex);
        Assert.Equal(text, category.TextHex);
    }
}
