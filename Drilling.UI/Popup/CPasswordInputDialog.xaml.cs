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
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string ResultPassword => PasswordInput.Password;

    private void BuildKeys()
    {
        foreach (var key in "1234567890QWERTYUIOPASDFGHJKLZXCVBNM_-./:".Select(value => value.ToString()))
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

    private void SpaceClick(object sender, RoutedEventArgs e) => PasswordInput.Password += " ";

    private void BackClick(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password.Length > 0) PasswordInput.Password = PasswordInput.Password[..^1];
    }

    private void ClearClick(object sender, RoutedEventArgs e) => PasswordInput.Clear();

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OkClick(object sender, RoutedEventArgs e) => DialogResult = true;

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
