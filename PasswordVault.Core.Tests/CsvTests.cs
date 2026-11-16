using System.Text;
using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class CsvTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-03-04T05:06:07+00:00");

    [Fact]
    public void DetectColumnsChromeHeader()
    {
        using var stream = Utf8("name,url,username,password,note\r\n");

        var columns = CsvImporter.DetectColumns(stream);

        Assert.Equal("name", columns[CsvField.Title]);
        Assert.Equal("url", columns[CsvField.Website]);
        Assert.Equal("username", columns[CsvField.Username]);
        Assert.Equal("password", columns[CsvField.Password]);
        Assert.Equal("note", columns[CsvField.Notes]);
    }

    [Fact]
    public void ImportExcelFileDetectsBomAndSemicolonDelimiter()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("name;url;username;password;note\r\n\"Mail;personal\";https://mail.example;alice;secret;primary\r\n"))
            .ToArray());
        var columns = CsvImporter.DetectColumns(stream);

        var result = CsvImporter.Read(stream, columns, [], [], Now);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Mail;personal", entry.Title);
        Assert.Equal("https://mail.example", entry.Website);
        Assert.Equal("alice", entry.Username);
        Assert.Equal("secret", entry.Password);
        Assert.Equal("primary", entry.Notes);
    }

    [Fact]
    public void ImportSkipsRowsMissingRequiredValuesAndCountsThem()
    {
        using var stream = Utf8(
            "title,url,username,password,notes\r\n" +
            "Valid,https://valid.example,alice,secret,keep\r\n" +
            ",https://no-title.example,bob,secret,skip\r\n" +
            "No username,https://no-user.example,,secret,skip\r\n" +
            "No password,https://no-password.example,carol,,skip\r\n");
        var columns = CsvImporter.DetectColumns(stream);

        var result = CsvImporter.Read(stream, columns, [], [], Now);

        Assert.Single(result.Entries);
        Assert.Equal(3, result.SkippedRows);
    }

    [Fact]
    public void ImportSkipsExactDuplicates()
    {
        var existing = new[]
        {
            CreateEntry("Existing", "https://same.example", "alice", "secret")
        };
        using var stream = Utf8(
            "title,url,username,password\r\n" +
            "Duplicate,https://same.example,alice,secret\r\n" +
            "New,https://new.example,bob,new-secret\r\n");
        var columns = CsvImporter.DetectColumns(stream);

        var result = CsvImporter.Read(stream, columns, existing, [], Now);

        Assert.Single(result.Entries);
        Assert.Equal("New", result.Entries[0].Title);
        Assert.Equal(1, result.DuplicateRows);
    }

    [Fact]
    public void ImportCreatesNewCategoriesByNameCaseInsensitively()
    {
        var existingCategory = CreateCategory("Email");
        using var stream = Utf8(
            "title,url,username,password,category\r\n" +
            "One,https://one.example,alice,secret,eMaIl\r\n" +
            "Two,https://two.example,bob,other,Travel\r\n");
        var columns = CsvImporter.DetectColumns(stream);

        var result = CsvImporter.Read(stream, columns, [], [existingCategory], Now);

        var newCategory = Assert.Single(result.NewCategories);
        Assert.Equal("Travel", newCategory.Name);
        Assert.Equal(existingCategory.Id, result.Entries[0].CategoryId);
        Assert.Equal(newCategory.Id, result.Entries[1].CategoryId);
    }

    [Fact]
    public void RoundTripPreservesAllEntryValuesAndCustomFields()
    {
        var category = CreateCategory("Travel");
        var passwords = new[] { "=1+1", "a,b", "first line\nsecond line", "كلمة المرور", "🔐 password" };
        var entries = passwords.Select((password, index) =>
        {
            var entry = CreateEntry($"Entry {index}", $"https://{index}.example", $"user{index}", password);
            entry.CategoryId = category.Id;
            entry.Notes = $"note {index}, with newline\nand emoji 🌟";
            entry.CustomFields =
            [
                new CustomField { Name = "field, one", Value = password, IsHidden = true },
                new CustomField { Name = "field two", Value = "خطوة 🔑\nnext", IsHidden = false }
            ];
            return entry;
        }).ToList();
        using var stream = new MemoryStream();

        CsvExporter.Write(entries, [category], stream);
        stream.Position = 0;
        var columns = CsvImporter.DetectColumns(stream);
        var result = CsvImporter.Read(stream, columns, [], [category], Now);

        Assert.Equal(entries.Count, result.Entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            AssertEntryValuesEqual(entries[index], result.Entries[index]);
        }

        var bytes = stream.ToArray();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..Encoding.UTF8.GetPreamble().Length]);
    }

    [Fact]
    public void ExportExcludesDeletedEntries()
    {
        var active = CreateEntry("Active", "https://active.example", "alice", "active-secret");
        var deleted = CreateEntry("Deleted", "https://deleted.example", "bob", "deleted-secret");
        deleted.IsDeleted = true;
        using var stream = new MemoryStream();

        CsvExporter.Write([active, deleted], [], stream);
        stream.Position = 0;
        var columns = CsvImporter.DetectColumns(stream);
        var result = CsvImporter.Read(stream, columns, [], [], Now);

        Assert.Single(result.Entries);
        Assert.Equal("Active", result.Entries[0].Title);
    }

    [Fact]
    public void ExportWritesExpectedHeader()
    {
        using var stream = new MemoryStream();

        CsvExporter.Write([], [], stream);

        var csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.StartsWith(Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble()) +
            "title,category,url,username,password,notes,custom_fields", csv);
    }

    private static MemoryStream Utf8(string content) => new(Encoding.UTF8.GetBytes(content));

    private static VaultEntry CreateEntry(string title, string website, string username, string password)
    {
        return new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = title,
            Website = website,
            Username = username,
            Password = password,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static Category CreateCategory(string name)
    {
        return new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            BackgroundHex = "#E1F5EE",
            TextHex = "#085041",
            IconKey = "folder",
            UpdatedAt = Now
        };
    }

    private static void AssertEntryValuesEqual(VaultEntry expected, VaultEntry actual)
    {
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Website, actual.Website);
        Assert.Equal(expected.Username, actual.Username);
        Assert.Equal(expected.Password, actual.Password);
        Assert.Equal(expected.Notes, actual.Notes);
        Assert.Equal(expected.CategoryId, actual.CategoryId);
        Assert.Equal(expected.CustomFields.Count, actual.CustomFields.Count);
        for (var index = 0; index < expected.CustomFields.Count; index++)
        {
            Assert.Equal(expected.CustomFields[index].Name, actual.CustomFields[index].Name);
            Assert.Equal(expected.CustomFields[index].Value, actual.CustomFields[index].Value);
            Assert.Equal(expected.CustomFields[index].IsHidden, actual.CustomFields[index].IsHidden);
        }
    }
}
