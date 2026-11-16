using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace PasswordVault.Core;

public static class CsvImporter
{
    private static readonly IReadOnlyDictionary<CsvField, string[]> HeaderAliases =
        new Dictionary<CsvField, string[]>
        {
            [CsvField.Title] = ["title", "name"],
            [CsvField.Category] = ["category"],
            [CsvField.Website] = ["website", "url", "login_uri", "origin", "uri"],
            [CsvField.Username] = ["username", "user_name", "login_username", "email"],
            [CsvField.Password] = ["password", "password_value"],
            [CsvField.Notes] = ["notes", "note"],
            [CsvField.CustomFields] = ["custom_fields"]
        };

    public static Dictionary<CsvField, string> DetectColumns(Stream stream)
    {
        var headers = ReadHeaders(stream);
        var result = new Dictionary<CsvField, string>();
        foreach (var (field, aliases) in HeaderAliases)
        {
            var match = headers.FirstOrDefault(header =>
                aliases.Contains(header.Trim(), StringComparer.OrdinalIgnoreCase));
            if (match is not null)
            {
                result[field] = match;
            }
        }

        return result;
    }

    public static IReadOnlyList<string> ReadHeaders(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var originalPosition = stream.CanSeek ? stream.Position : (long?)null;

        try
        {
            using var reader = CreateReader(stream);
            using var csv = new CsvReader(reader, CreateConfiguration());
            if (!csv.Read())
            {
                return [];
            }

            csv.ReadHeader();
            return csv.HeaderRecord ?? [];
        }
        finally
        {
            if (originalPosition.HasValue)
            {
                stream.Position = originalPosition.Value;
            }
        }
    }

    public static CsvImportResult Read(
        Stream stream,
        Dictionary<CsvField, string> map,
        IReadOnlyList<VaultEntry> existing,
        IReadOnlyList<Category> categories,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(categories);

        var entries = new List<VaultEntry>();
        var newCategories = new List<Category>();
        var skippedRows = 0;
        var duplicateRows = 0;
        var usedCategories = categories
            .Where(category => !category.IsDeleted)
            .ToList();
        var seenEntries = existing
            .Where(entry => !entry.IsDeleted)
            .Select(entry => (entry.Website, entry.Username, entry.Password))
            .ToHashSet();

        using var reader = CreateReader(stream);
        using var csv = new CsvReader(reader, CreateConfiguration());
        if (!csv.Read())
        {
            return new CsvImportResult(entries, newCategories, skippedRows, duplicateRows);
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        var fieldIndexes = map
            .Select(pair => (pair.Key, Index: Array.FindIndex(headers, header =>
                string.Equals(header, pair.Value, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Index >= 0)
            .ToDictionary(pair => pair.Key, pair => pair.Index);

        while (csv.Read())
        {
            string Value(CsvField field)
            {
                return fieldIndexes.TryGetValue(field, out var index)
                    ? csv.GetField(index) ?? string.Empty
                    : string.Empty;
            }

            var title = Value(CsvField.Title);
            var username = Value(CsvField.Username);
            var password = Value(CsvField.Password);
            if (string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(username)
                || string.IsNullOrWhiteSpace(password))
            {
                skippedRows++;
                continue;
            }

            var website = Value(CsvField.Website);
            if (!seenEntries.Add((website, username, password)))
            {
                duplicateRows++;
                continue;
            }

            var categoryId = ResolveCategoryId(Value(CsvField.Category), usedCategories, newCategories, now);
            var customFieldsText = Value(CsvField.CustomFields);
            var customFields = string.IsNullOrEmpty(customFieldsText)
                ? []
                : JsonSerializer.Deserialize<List<CustomField>>(customFieldsText) ?? [];

            entries.Add(new VaultEntry
            {
                Id = Guid.NewGuid(),
                Title = title,
                Website = website,
                Username = username,
                Password = password,
                Notes = Value(CsvField.Notes),
                CategoryId = categoryId,
                CustomFields = customFields,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return new CsvImportResult(entries, newCategories, skippedRows, duplicateRows);
    }

    private static Guid ResolveCategoryId(
        string name,
        List<Category> categories,
        List<Category> newCategories,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Guid.Empty;
        }

        var existing = categories.FirstOrDefault(category =>
            string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = newCategories.FirstOrDefault(category =>
            string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));
        if (created is not null)
        {
            return created.Id;
        }

        created = new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            BackgroundHex = "#E1F5EE",
            TextHex = "#085041",
            IconKey = "folder",
            UpdatedAt = now
        };
        newCategories.Add(created);
        return created.Id;
    }

    private static StreamReader CreateReader(Stream stream)
    {
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    }

    private static CsvConfiguration CreateConfiguration()
    {
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t", "|"],
            IgnoreBlankLines = true
        };
    }
}
