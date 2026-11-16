using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace PasswordVault.Core;

public static class CsvExporter
{
    private static readonly string[] Headers =
    [
        "title",
        "category",
        "url",
        "username",
        "password",
        "notes",
        "custom_fields"
    ];

    public static void Write(
        IEnumerable<VaultEntry> entries,
        IEnumerable<Category> categories,
        Stream stream)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(stream);

        var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false
        });

        foreach (var header in Headers)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();
        foreach (var entry in entries.Where(entry => !entry.IsDeleted))
        {
            csv.WriteField(entry.Title);
            csv.WriteField(categoryNames.GetValueOrDefault(entry.CategoryId, string.Empty));
            csv.WriteField(entry.Website);
            csv.WriteField(entry.Username);
            csv.WriteField(entry.Password);
            csv.WriteField(entry.Notes);
            csv.WriteField(JsonSerializer.Serialize(entry.CustomFields));
            csv.NextRecord();
        }
    }
}
