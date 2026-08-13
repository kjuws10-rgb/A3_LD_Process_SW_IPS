using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Drilling.UI.Popup;

public partial class CPasswordInputDialog : Window
{
    public CPasswordInputDialog(string password = "")
    {
        InitializeComponent();
        PasswordInput.Password = password;
        BuildKeys();
        void LoadedHandler1(object unusedParameter1, RoutedEventArgs unusedParameter2)
        {
            PasswordInput.Focus();
        }

        Loaded += LoadedHandler1;
    }

    public string ResultPassword
    {
        get
        {
            return PasswordInput.Password;
        }
    }

    private void BuildKeys()
    {
        string SelectValue2(char value)
        {
            return value.ToString();
        }

        foreach (var key in "1234567890QWERTYUIOPASDFGHJKLZXCVBNM_-./:".Select(SelectValue2))
        {
            var button = new Button
            {
                Content = key,
                Tag = key,
                Style = (Style)FindResource("KeyButton")
            };
            button.Click += KeyClick;
            KeyGrid.Children.Add(button);
        }
    }

    private void KeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) PasswordInput.Password += key;
    }

    private void SpaceClick(object sender, RoutedEventArgs e)
    {
        PasswordInput.Password += " ";
    }

    private void BackClick(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password.Length > 0) PasswordInput.Password = PasswordInput.Password[..^1];
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        PasswordInput.Clear();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            DialogResult = true;
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }
}
