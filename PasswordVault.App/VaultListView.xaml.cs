namespace PasswordVault.App;

public partial class VaultListView : ContentView
{
    private readonly VaultListViewModel _viewModel;

    public VaultListView(VaultListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    public event EventHandler? AddRequested;
    public event EventHandler<VaultEntrySelectedEventArgs>? EntrySelected;

    public void Refresh() => _viewModel.Refresh();

    private void OnCategoryClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: VaultListCategoryChip chip })
        {
            _viewModel.SelectedCategoryId = chip.CategoryId;
        }
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        AddRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEntryTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid { BindingContext: VaultListEntryItem item })
        {
            EntrySelected?.Invoke(this, new VaultEntrySelectedEventArgs(item.Entry));
        }
    }
}

public sealed class VaultEntrySelectedEventArgs(PasswordVault.Core.VaultEntry entry) : EventArgs
{
    public PasswordVault.Core.VaultEntry Entry { get; } = entry;
}
