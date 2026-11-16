using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PasswordVault.App;

public sealed class CustomFieldDraft : INotifyPropertyChanged
{
    private string _name;
    private string _value;
    private bool _isHidden;

    public CustomFieldDraft(string name, string value, bool isHidden)
    {
        _name = name;
        _value = value;
        _isHidden = isHidden;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool IsHidden
    {
        get => _isHidden;
        set => SetProperty(ref _isHidden, value);
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
