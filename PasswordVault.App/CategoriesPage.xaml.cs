using PasswordVault.Core;

namespace PasswordVault.App;

public partial class CategoriesPage : ContentPage
{
    private static readonly ColorPreset[] Presets =
    [
        new("Mint", "#E1F5EE", "#085041"),
        new("Sand", "#FAEEDA", "#633806"),
        new("Rose", "#FBEAF0", "#72243E"),
        new("Lavender", "#EEEDFE", "#3C3489"),
        new("Peach", "#FAECE7", "#712B13"),
        new("Blue", "#E6F1FB", "#173D6B"),
        new("Purple", "#F2E8FC", "#5A2D82"),
        new("Slate", "#EDF0F2", "#33393E")
    ];

    private readonly VaultService _vaultService;
    private readonly IClock _clock;

    public CategoriesPage(VaultService vaultService, IClock clock)
    {
        InitializeComponent();
        _vaultService = vaultService;
        _clock = clock;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshCategories();
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Add category", "Category name");
        if (name is null)
        {
            return;
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            await DisplayAlertAsync("Invalid name", "Enter a category name.", "OK");
            return;
        }

        var preset = Presets[0];
        _vaultService.Data.Categories.Add(new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            BackgroundHex = preset.BackgroundHex,
            TextHex = preset.TextHex,
            IconKey = "folder",
            UpdatedAt = _clock.UtcNow
        });
        await SaveAndRefreshAsync();
    }

    private async void OnManageClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Category category })
        {
            return;
        }

        var action = await DisplayActionSheetAsync(
            category.Name,
            "Cancel",
            "Delete",
            "Rename",
            "Recolor");

        switch (action)
        {
            case "Rename":
                await RenameAsync(category);
                break;
            case "Recolor":
                await RecolorAsync(category);
                break;
            case "Delete":
                await DeleteAsync(category);
                break;
        }
    }

    private async Task RenameAsync(Category category)
    {
        var name = await DisplayPromptAsync("Rename category", "Category name", initialValue: category.Name);
        if (name is null)
        {
            return;
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            await DisplayAlertAsync("Invalid name", "Enter a category name.", "OK");
            return;
        }

        category.Name = name;
        category.UpdatedAt = _clock.UtcNow;
        await SaveAndRefreshAsync();
    }

    private async Task RecolorAsync(Category category)
    {
        var selectedName = await DisplayActionSheetAsync(
            "Choose a color",
            "Cancel",
            null,
            Presets.Select(preset => preset.Name).ToArray());
        var selectedPreset = Presets.FirstOrDefault(preset => preset.Name == selectedName);
        if (selectedPreset is null)
        {
            return;
        }

        category.BackgroundHex = selectedPreset.BackgroundHex;
        category.TextHex = selectedPreset.TextHex;
        category.UpdatedAt = _clock.UtcNow;
        await SaveAndRefreshAsync();
    }

    private async Task DeleteAsync(Category category)
    {
        var confirmed = await DisplayAlertAsync(
            "Delete category",
            $"Delete {category.Name}? Entries in it will move to All.",
            "Delete",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        category.IsDeleted = true;
        category.UpdatedAt = _clock.UtcNow;
        foreach (var entry in _vaultService.Data.Entries.Where(entry => entry.CategoryId == category.Id))
        {
            entry.CategoryId = Guid.Empty;
        }

        await SaveAndRefreshAsync();
    }

    private async Task SaveAndRefreshAsync()
    {
        try
        {
            _vaultService.Save();
            RefreshCategories();
        }
        catch (IOException)
        {
            await DisplayAlertAsync("Save failed", "The category change could not be saved.", "OK");
        }
    }

    private void RefreshCategories()
    {
        CategoryList.Children.Clear();
        foreach (var category in _vaultService.Data.Categories.Where(category => !category.IsDeleted))
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(12, 8)
            };
            row.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = ParseColor(category.BackgroundHex, Colors.LightGray),
                        HeightRequest = 24,
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                        WidthRequest = 24
                    },
                    new Label
                    {
                        Text = category.Name,
                        TextColor = ParseColor(category.TextHex, Colors.Black),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            });
            var manageButton = new Button
            {
                Text = "Manage",
                Padding = new Thickness(12, 6),
                BindingContext = category,
                VerticalOptions = LayoutOptions.Center
            };
            manageButton.Clicked += OnManageClicked;
            row.Add(manageButton, 1);
            CategoryList.Add(new Border
            {
                Stroke = Color.FromArgb("#D7E1E4"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Content = row
            });
        }
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        if (hex.Length != 7 || hex[0] != '#' ||
            !uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return fallback;
        }

        return Color.FromArgb(hex);
    }

    private sealed record ColorPreset(string Name, string BackgroundHex, string TextHex);
}
