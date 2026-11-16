using PasswordVault.Core;

namespace PasswordVault.App;

public partial class EntryDetailView : ContentView
{
    private readonly VaultService _vaultService;
    private readonly IClipboardService _clipboardService;
    private VaultEntry? _entry;
    private bool _isPasswordVisible;

    public EntryDetailView(VaultService vaultService, IClipboardService clipboardService)
    {
        InitializeComponent();
        _vaultService = vaultService;
        _clipboardService = clipboardService;
    }

    public void LoadEntry(VaultEntry entry)
    {
        _entry = entry;
        TitleLabel.Text = entry.Title;
        WebsiteLabel.Text = entry.Website;
        UsernameLabel.Text = entry.Username;
        NotesLabel.Text = entry.Notes;
        _isPasswordVisible = false;
        UpdatePasswordValue();

        var category = _vaultService.Data.Categories.FirstOrDefault(
            item => item.Id == entry.CategoryId && !item.IsDeleted);
        CategoryLabel.Text = category?.Name ?? "Uncategorized";
        CategoryLabel.BackgroundColor = ParseColor(category?.BackgroundHex, "#EAF4F6");
        CategoryLabel.TextColor = ParseColor(category?.TextHex, "#0B6E8F");

        CustomFieldsList.Children.Clear();
        foreach (var field in entry.CustomFields)
        {
            AddCustomField(field);
        }
    }

    private void AddCustomField(CustomField field)
    {
        var valueLabel = new Label
        {
            Text = field.IsHidden ? Mask(field.Value) : field.Value,
            VerticalOptions = LayoutOptions.Center
        };
        var valueRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        valueRow.Add(valueLabel);

        if (field.IsHidden)
        {
            var isVisible = false;
            var eyeButton = new Button
            {
                Text = "👁",
                Padding = new Thickness(12, 4)
            };
            eyeButton.Clicked += (_, _) =>
            {
                isVisible = !isVisible;
                valueLabel.Text = isVisible ? field.Value : Mask(field.Value);
            };
            valueRow.Add(eyeButton, 1);
        }

        CustomFieldsList.Add(new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = field.Name, FontSize = 12, TextColor = Color.FromArgb("#52666E") },
                valueRow
            }
        });
    }

    private void OnPasswordEyeClicked(object? sender, EventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        UpdatePasswordValue();
    }

    private async void OnCopyUsernameClicked(object? sender, EventArgs e)
    {
        if (_entry is not null)
        {
            await _clipboardService.CopySecret(
                _entry.Username,
                _vaultService.Data.Settings.ClipboardClearSeconds);
        }
    }

    private async void OnCopyPasswordClicked(object? sender, EventArgs e)
    {
        if (_entry is not null)
        {
            await _clipboardService.CopySecret(
                _entry.Password,
                _vaultService.Data.Settings.ClipboardClearSeconds);
        }
    }

    private void UpdatePasswordValue()
    {
        if (_entry is null)
        {
            return;
        }

        PasswordLabel.Text = _isPasswordVisible ? _entry.Password : Mask(_entry.Password);
        PasswordEyeButton.Text = _isPasswordVisible ? "🙈" : "👁";
    }

    private static string Mask(string value) => new('•', value.Length);

    private static Color ParseColor(string? hex, string fallback)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#' ||
            !uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return Color.FromArgb(fallback);
        }

        return Color.FromArgb(hex);
    }
}
