using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PasswordVault.Core;

namespace PasswordVault.App;

public sealed class VaultListViewModel : INotifyPropertyChanged
{
    private readonly VaultService _vaultService;
    private string _searchText = string.Empty;
    private Guid? _selectedCategoryId;

    public VaultListViewModel(VaultService vaultService)
    {
        _vaultService = vaultService;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VaultListCategoryChip> Categories { get; } = [];
    public ObservableCollection<VaultListEntryItem> Entries { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value ?? string.Empty;
            OnPropertyChanged();
            RefreshEntries();
        }
    }

    public Guid? SelectedCategoryId
    {
        get => _selectedCategoryId;
        set
        {
            if (_selectedCategoryId == value)
            {
                return;
            }

            _selectedCategoryId = value;
            RefreshCategories();
            RefreshEntries();
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        var activeCategories = _vaultService.Data.Categories
            .Where(category => !category.IsDeleted)
            .ToList();

        if (_selectedCategoryId is Guid selectedId &&
            activeCategories.All(category => category.Id != selectedId))
        {
            _selectedCategoryId = null;
        }

        Categories.Clear();
        Categories.Add(VaultListCategoryChip.All(_selectedCategoryId is null));
        foreach (var category in activeCategories)
        {
            Categories.Add(new VaultListCategoryChip(category, _selectedCategoryId == category.Id));
        }

        RefreshEntries(activeCategories);
    }

    private void RefreshCategories()
    {
        var activeCategories = _vaultService.Data.Categories
            .Where(category => !category.IsDeleted)
            .ToList();

        Categories.Clear();
        Categories.Add(VaultListCategoryChip.All(_selectedCategoryId is null));
        foreach (var category in activeCategories)
        {
            Categories.Add(new VaultListCategoryChip(category, _selectedCategoryId == category.Id));
        }
    }

    private void RefreshEntries(List<Category>? activeCategories = null)
    {
        activeCategories ??= _vaultService.Data.Categories
            .Where(category => !category.IsDeleted)
            .ToList();
        var search = SearchText.Trim();
        var categoryById = activeCategories.ToDictionary(category => category.Id);

        var filteredEntries = _vaultService.Data.Entries
            .Where(entry => !entry.IsDeleted)
            .Where(entry => _selectedCategoryId is null || entry.CategoryId == _selectedCategoryId)
            .Where(entry => search.Length == 0 ||
                entry.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Website.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Username.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                categoryById.TryGetValue(entry.CategoryId, out var category);
                return new VaultListEntryItem(entry, category);
            })
            .ToList();

        Entries.Clear();
        foreach (var entry in filteredEntries)
        {
            Entries.Add(entry);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class VaultListCategoryChip
{
    private const string SeaBlue = "#0B6E8F";

    private VaultListCategoryChip(
        Guid? categoryId,
        string name,
        string backgroundHex,
        string textHex,
        bool isSelected)
    {
        CategoryId = categoryId;
        Name = name;
        var background = ParseColor(backgroundHex, Colors.LightGray);
        var text = ParseColor(textHex, Colors.Black);
        BackgroundColor = categoryId is null && !isSelected
            ? ParseColor("#EAF4F6", Colors.LightGray)
            : background;
        TextColor = categoryId is null && !isSelected
            ? ParseColor(SeaBlue, Colors.Black)
            : text;
        BorderColor = isSelected ? text : Colors.Transparent;
        BorderWidth = isSelected ? 2 : 0;
    }

    public Guid? CategoryId { get; }
    public string Name { get; }
    public Color BackgroundColor { get; }
    public Color TextColor { get; }
    public Color BorderColor { get; }
    public double BorderWidth { get; }

    public static VaultListCategoryChip All(bool isSelected) =>
        new(null, "All", SeaBlue, "#FFFFFF", isSelected);

    public VaultListCategoryChip(Category category, bool isSelected)
        : this(
            category.Id,
            category.Name,
            category.BackgroundHex,
            category.TextHex,
            isSelected)
    {
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
}

public sealed class VaultListEntryItem
{
    public VaultListEntryItem(VaultEntry entry, Category? category)
    {
        Title = entry.Title;
        Username = entry.Username;
        CategoryBackgroundColor = ParseColor(category?.BackgroundHex, "#EAF4F6");
        CategoryTextColor = ParseColor(category?.TextHex, "#0B6E8F");
        IconGlyph = GetIconGlyph(category?.IconKey);
    }

    public string Title { get; }
    public string Username { get; }
    public Color CategoryBackgroundColor { get; }
    public Color CategoryTextColor { get; }
    public string IconGlyph { get; }

    private static string GetIconGlyph(string? iconKey) => iconKey switch
    {
        "email" => "✉",
        "banking" => "▣",
        "shopping" => "🛍",
        "social" => "◎",
        "work" => "▤",
        "folder" => "▰",
        _ => "◆"
    };

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
