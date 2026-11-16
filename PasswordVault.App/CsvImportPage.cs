using System.Text.Json;
using CsvHelper;
using PasswordVault.Core;

namespace PasswordVault.App;

public sealed class CsvImportPage : ContentPage
{
    private readonly VaultService _vaultService;
    private readonly byte[] _csvBytes;
    private readonly Dictionary<CsvField, Picker> _pickers = [];
    private readonly Label _previewLabel = new();

    public CsvImportPage(VaultService vaultService, byte[] csvBytes)
    {
        _vaultService = vaultService;
        _csvBytes = csvBytes;
        Title = "Import CSV";

        var headers = CsvImporter.ReadHeaders(new MemoryStream(_csvBytes));
        var detectedColumns = CsvImporter.DetectColumns(new MemoryStream(_csvBytes));
        var mappingLayout = new VerticalStackLayout { Spacing = 8 };
        foreach (var field in Enum.GetValues<CsvField>())
        {
            var picker = new Picker { Title = "Select column" };
            picker.Items.Add("Not mapped");
            picker.SelectedIndex = 0;
            foreach (var header in headers)
            {
                picker.Items.Add(header);
            }

            if (detectedColumns.TryGetValue(field, out var detectedHeader))
            {
                var headerIndex = headers.ToList().FindIndex(header =>
                    string.Equals(header, detectedHeader, StringComparison.Ordinal));
                if (headerIndex >= 0)
                {
                    picker.SelectedIndex = headerIndex + 1;
                }
            }

            picker.SelectedIndexChanged += (_, _) => RefreshPreview();
            _pickers.Add(field, picker);
            mappingLayout.Children.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label { Text = field.ToString(), FontAttributes = FontAttributes.Bold },
                    picker
                }
            });
        }

        var importButton = new Button
        {
            Text = "Import",
            BackgroundColor = Color.FromArgb("#0B6E8F"),
            TextColor = Colors.White
        };
        importButton.Clicked += OnImportClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                Children =
                {
                    new Label { Text = "Map each vault field to a CSV column." },
                    mappingLayout,
                    new Label { Text = "Preview (up to 5 rows)", FontAttributes = FontAttributes.Bold },
                    _previewLabel,
                    importButton
                }
            }
        };

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        try
        {
            var result = CsvImporter.Read(
                new MemoryStream(_csvBytes),
                GetMap(),
                [],
                _vaultService.Data.Categories,
                DateTimeOffset.UtcNow);
            var rows = result.Entries.Take(5)
                .Select(entry => $"{entry.Title} | {entry.Username} | {entry.Website}")
                .ToArray();
            _previewLabel.Text = rows.Length == 0
                ? "No preview rows. Check the Title, Username, and Password mappings."
                : string.Join(Environment.NewLine, rows);
        }
        catch (CsvHelperException)
        {
            _previewLabel.Text = "Could not preview this file. Check that it is a valid CSV.";
        }
        catch (JsonException)
        {
            _previewLabel.Text = "Could not preview custom fields because their JSON is invalid.";
        }
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        CsvImportResult result;
        try
        {
            result = CsvImporter.Read(
                new MemoryStream(_csvBytes),
                GetMap(),
                _vaultService.Data.Entries,
                _vaultService.Data.Categories,
                DateTimeOffset.UtcNow);
        }
        catch (CsvHelperException)
        {
            await DisplayAlertAsync("Import failed", "Check that the selected file is a valid CSV.", "OK");
            return;
        }
        catch (JsonException)
        {
            await DisplayAlertAsync("Import failed", "The custom_fields column contains invalid JSON.", "OK");
            return;
        }

        _vaultService.Data.Categories.AddRange(result.NewCategories);
        _vaultService.Data.Entries.AddRange(result.Entries);
        _vaultService.Save();
        await DisplayAlertAsync(
            "Import complete",
            $"Imported {result.Entries.Count}, skipped {result.SkippedRows}, duplicates {result.DuplicateRows}",
            "OK");
        await Navigation.PopAsync();
    }

    private Dictionary<CsvField, string> GetMap()
    {
        var result = new Dictionary<CsvField, string>();
        foreach (var (field, picker) in _pickers)
        {
            if (picker.SelectedIndex > 0)
            {
                result[field] = picker.Items[picker.SelectedIndex];
            }
        }

        return result;
    }
}
