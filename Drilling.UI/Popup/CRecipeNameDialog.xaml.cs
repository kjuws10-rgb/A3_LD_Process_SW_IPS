using System.Windows;
using System.Windows.Input;
using Drilling.Common.Managers;

namespace Drilling.UI.Popup;

public partial class CRecipeNameDialog : Window
{
    private readonly Func<string, string>? _validate;

    public CRecipeNameDialog(
        string title,
        string message,
        string initialName,
        Func<string, string>? validate = null)
    {
        InitializeComponent();

        _validate = validate;
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        RecipeNameTextBox.Text = initialName;
        RecipeNameTextBox.SelectAll();
        RecipeNameTextBox.Focus();
        UpdateOkState();
    }

    public string RecipeName
    {
        get
        {
            return RecipeNameTextBox.Text.Trim();
        }
    }

    private void OpenScreenKeyboard(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var dialog = new CValueInputDialog(TitleText.Text, RecipeNameTextBox.Text, EN_RECIPE_DATA_TYPE.String, 0, 0)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;
        RecipeNameTextBox.Text = dialog.ResultValue;
        RecipeNameTextBox.CaretIndex = RecipeNameTextBox.Text.Length;
    }

    private void OnRecipeNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateOkState();
    }

    private void OnRecipeNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OkButton.IsEnabled)
        {
            DialogResult = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateOkState()
    {
        var message = _validate?.Invoke(RecipeNameTextBox.Text.Trim())
            ?? (string.IsNullOrWhiteSpace(RecipeNameTextBox.Text) ? "Recipe name is required." : "");

        ErrorText.Text = message;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
        OkButton.IsEnabled = string.IsNullOrWhiteSpace(message);
    }
}

