using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Drilling.UI.Popup;

namespace Drilling.UI.Views;

public partial class CPmView : UserControl
{
    public CPmView()
    {
        InitializeComponent();
    }

    private void OpenPmPasswordKeyboard(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var dialog = new CPasswordInputDialog(PmPasswordBox.Password)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            PmPasswordBox.Password = dialog.ResultPassword;
        }
    }

    private void ClearPmPassword(object sender, RoutedEventArgs e)
    {
        PmPasswordBox.Clear();
    }
}
