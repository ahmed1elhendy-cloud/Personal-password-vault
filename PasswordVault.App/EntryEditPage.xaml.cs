using System.Collections.ObjectModel;
using PasswordVault.Core;

namespace PasswordVault.App;

public partial class EntryEditPage : ContentPage
{
    private readonly VaultService _vaultService;
    private readonly IClock _clock;
    private readonly ObservableCollection<CustomFieldDraft> _customFields = [];
    private VaultEntry? _entry;

    public EntryEditPage(VaultService vaultService, IClock clock)
    {
        InitializeComponent();
        _vaultService = vaultService;
        _clock = clock;
        CategoryPicker.ItemsSource = _vaultService.Data.Categories
            .Where(category => !category.IsDeleted)
            .ToList();
        CategoryPicker.ItemDisplayBinding = new Binding(nameof(Category.Name));
        BindableLayout.SetItemsSource(CustomFieldsList, _customFields);
        CategoryPicker.SelectedItem = CategoryPicker.ItemsSource.Cast<Category>().FirstOrDefault();
    }

    public void LoadEntry(VaultEntry entry)
    {
        _entry = entry;
        Title = "Edit entry";
        TitleEntry.Text = entry.Title;
        WebsiteEntry.Text = entry.Website;
        UsernameEntry.Text = entry.Username;
        PasswordEntry.Text = entry.Password;
        NotesEntry.Text = entry.Notes;
        CategoryPicker.SelectedItem = CategoryPicker.ItemsSource
            .Cast<Category>()
            .FirstOrDefault(category => category.Id == entry.CategoryId);
        DeleteButton.IsVisible = true;

        _customFields.Clear();
        foreach (var field in entry.CustomFields)
        {
            _customFields.Add(new CustomFieldDraft(field.Name, field.Value, field.IsHidden));
        }
    }

    private void OnGeneratorOptionsClicked(object? sender, EventArgs e)
    {
        GeneratorPanel.IsVisible = !GeneratorPanel.IsVisible;
    }

    private async void OnGenerateClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(GeneratorLengthEntry.Text, out var length) || length is < 8 or > 64)
        {
            await DisplayAlertAsync("Invalid length", "Choose a length from 8 to 64.", "OK");
            return;
        }

        if (!IncludeLowercaseSwitch.IsToggled &&
            !IncludeUppercaseSwitch.IsToggled &&
            !IncludeDigitsSwitch.IsToggled &&
            !IncludeSymbolsSwitch.IsToggled)
        {
            await DisplayAlertAsync("Choose a character class", "Select at least one class.", "OK");
            return;
        }

        PasswordEntry.Text = PasswordGenerator.Generate(
            length,
            IncludeLowercaseSwitch.IsToggled,
            IncludeUppercaseSwitch.IsToggled,
            IncludeDigitsSwitch.IsToggled,
            IncludeSymbolsSwitch.IsToggled);
        GeneratorPanel.IsVisible = false;
    }

    private void OnAddFieldClicked(object? sender, EventArgs e)
    {
        _customFields.Add(new CustomFieldDraft(string.Empty, string.Empty, false));
    }

    private void OnRemoveFieldClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: CustomFieldDraft field })
        {
            _customFields.Remove(field);
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var title = TitleEntry.Text?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            await DisplayAlertAsync("Title required", "Enter a title for this entry.", "OK");
            return;
        }

        var now = _clock.UtcNow.ToUniversalTime();
        var category = CategoryPicker.SelectedItem as Category;
        var draft = new VaultEntry
        {
            Title = title,
            Website = WebsiteEntry.Text?.Trim() ?? string.Empty,
            Username = UsernameEntry.Text ?? string.Empty,
            Password = PasswordEntry.Text ?? string.Empty,
            Notes = NotesEntry.Text ?? string.Empty,
            CategoryId = category?.Id ?? Guid.Empty,
            CustomFields = _customFields
                .Select(field => new CustomField
                {
                    Name = field.Name.Trim(),
                    Value = field.Value,
                    IsHidden = field.IsHidden
                })
                .ToList()
        };

        if (_entry is null)
        {
            draft.Id = Guid.NewGuid();
            draft.CreatedAt = now;
            draft.UpdatedAt = now;
            _vaultService.Data.Entries.Add(draft);
            try
            {
                _vaultService.Save();
            }
            catch (IOException)
            {
                _vaultService.Data.Entries.Remove(draft);
                await DisplayAlertAsync("Save failed", "The entry could not be saved.", "OK");
                return;
            }
        }
        else
        {
            var previous = Clone(_entry);
            ApplyDraft(_entry, draft);
            _entry.UpdatedAt = now;
            try
            {
                _vaultService.Save();
            }
            catch (IOException)
            {
                Restore(_entry, previous);
                await DisplayAlertAsync("Save failed", "The entry could not be saved.", "OK");
                return;
            }
        }

        await Navigation.PopAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_entry is null)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Delete entry",
            $"Delete {_entry.Title}?",
            "Delete",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        var previousUpdatedAt = _entry.UpdatedAt;
        var now = _clock.UtcNow.ToUniversalTime();
        _entry.IsDeleted = true;
        _entry.UpdatedAt = now;
        try
        {
            _vaultService.Save();
        }
        catch (IOException)
        {
            _entry.IsDeleted = false;
            _entry.UpdatedAt = previousUpdatedAt;
            await DisplayAlertAsync("Delete failed", "The entry could not be deleted.", "OK");
            return;
        }

        await Navigation.PopAsync();
    }

    private static void ApplyDraft(VaultEntry entry, VaultEntry draft)
    {
        entry.Title = draft.Title;
        entry.Website = draft.Website;
        entry.Username = draft.Username;
        entry.Password = draft.Password;
        entry.Notes = draft.Notes;
        entry.CategoryId = draft.CategoryId;
        entry.CustomFields = draft.CustomFields;
    }

    private static VaultEntry Clone(VaultEntry entry)
    {
        return new VaultEntry
        {
            Id = entry.Id,
            Title = entry.Title,
            Website = entry.Website,
            Username = entry.Username,
            Password = entry.Password,
            Notes = entry.Notes,
            CategoryId = entry.CategoryId,
            CustomFields = entry.CustomFields.Select(field => new CustomField
            {
                Name = field.Name,
                Value = field.Value,
                IsHidden = field.IsHidden
            }).ToList(),
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
            IsDeleted = entry.IsDeleted
        };
    }

    private static void Restore(VaultEntry entry, VaultEntry previous)
    {
        entry.Id = previous.Id;
        entry.Title = previous.Title;
        entry.Website = previous.Website;
        entry.Username = previous.Username;
        entry.Password = previous.Password;
        entry.Notes = previous.Notes;
        entry.CategoryId = previous.CategoryId;
        entry.CustomFields = previous.CustomFields;
        entry.CreatedAt = previous.CreatedAt;
        entry.UpdatedAt = previous.UpdatedAt;
        entry.IsDeleted = previous.IsDeleted;
    }
}
