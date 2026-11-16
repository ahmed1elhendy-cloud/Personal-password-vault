namespace PasswordVault.App;

internal sealed class PasswordPromptPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;

    private PasswordPromptPage(string title, string message)
    {
        Title = title;
        var passwordEntry = new Entry
        {
            Placeholder = "Master password",
            IsPassword = true,
            ReturnType = ReturnType.Done
        };
        var cancelButton = new Button { Text = "Cancel" };
        var confirmButton = new Button { Text = "Continue" };
        cancelButton.Clicked += async (_, _) => await CompleteAsync(null);
        confirmButton.Clicked += async (_, _) =>
            await CompleteAsync(string.IsNullOrEmpty(passwordEntry.Text) ? null : passwordEntry.Text);
        passwordEntry.Completed += async (_, _) =>
            await CompleteAsync(string.IsNullOrEmpty(passwordEntry.Text) ? null : passwordEntry.Text);

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold },
                new Label { Text = message },
                passwordEntry,
                new HorizontalStackLayout
                {
                    Spacing = 12,
                    Children = { cancelButton, confirmButton }
                }
            }
        };
    }

    internal static async Task<string?> PromptAsync(Page parent, string title, string message)
    {
        var page = new PasswordPromptPage(title, message);
        await parent.Navigation.PushModalAsync(page);
        return await page._completion.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CompleteAsync(null);
        return true;
    }

    private async Task CompleteAsync(string? value)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        await Navigation.PopModalAsync();
        _completion.TrySetResult(value);
    }
}
