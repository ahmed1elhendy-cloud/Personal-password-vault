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
}
