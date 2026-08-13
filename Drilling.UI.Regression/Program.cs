using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Drilling.UI;
using Drilling.UI.Menu;

namespace Drilling.UI.Regression;

internal static class Program
{
    private static int _executeCount;
    private static bool _canExecute = true;

    [STAThread]
    private static int Main()
    {
        try
        {
            TestNamedPropertyChangeBinding();
            TestButtonCommandBehavior();
            TestCoordinateBehavior();
            TestMenuBuildAndShutdown();
            Console.WriteLine("WPF_REGRESSION_PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"WPF_REGRESSION_FAIL {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void TestNamedPropertyChangeBinding()
    {
        CTestSource source = new CTestSource();
        TextBlock target = new TextBlock();
        Binding binding = new Binding("Value");
        binding.Source = source;
        BindingOperations.SetBinding(target, TextBlock.TextProperty, binding);
        source.Value = "updated";
        Assert(target.Text == "updated", "Named property event did not refresh the WPF binding.");
    }

    private static void TestButtonCommandBehavior()
    {
        CButtonCommand command = new CButtonCommand(ExecuteCommand, CanExecuteCommand);
        Button button = new Button();
        button.CommandParameter = "RUN";
        CButtonCommandBehavior.SetCommand(button, command);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(_executeCount == 1, "Button command did not execute once.");

        _canExecute = false;
        command.NotifyCanExecuteChanged();
        Assert(!button.IsEnabled, "Button CanExecute state was not refreshed.");
    }

    private static void TestCoordinateBehavior()
    {
        ContentPresenter presenter = new ContentPresenter();
        CPreviewCoordinateBehavior.SetPositionX(presenter, 50.0);
        CPreviewCoordinateBehavior.SetPositionY(presenter, 25.0);
        CPreviewCoordinateBehavior.SetContainerWidth(presenter, 200.0);
        CPreviewCoordinateBehavior.SetContainerHeight(presenter, 100.0);
        CPreviewCoordinateBehavior.SetDesignWidth(presenter, 100.0);
        CPreviewCoordinateBehavior.SetDesignHeight(presenter, 50.0);
        CPreviewCoordinateBehavior.SetElementWidth(presenter, 10.0);
        CPreviewCoordinateBehavior.SetElementHeight(presenter, 6.0);

        Assert(Canvas.GetLeft(presenter) == 95.0 && Canvas.GetTop(presenter) == 47.0,
            "Uniform coordinate behavior result changed.");

        CPreviewCoordinateBehavior.SetUseStretchScale(presenter, true);
        Assert(Canvas.GetLeft(presenter) == 95.0 && Canvas.GetTop(presenter) == 47.0,
            "Stretch coordinate behavior result changed.");
    }

    private static void TestMenuBuildAndShutdown()
    {
        CRootView? rootView = null;

        try
        {
            rootView = CAppStartup.CreateMainViewModel();

            foreach (var menuItem in rootView.Menus)
            {
                rootView.SelectedMenu = menuItem;
                Assert(
                    rootView.CurrentScreen.Menu == menuItem.Menu,
                    "Menu screen build result changed: " + menuItem.Menu);
            }
        }
        finally
        {
            CAppStartup.StopInitialization();
            rootView?.Shutdown();
        }
    }

    private static void ExecuteCommand(object? parameter)
    {
        Assert(Equals(parameter, "RUN"), "Command parameter changed.");
        _executeCount++;
    }

    private static bool CanExecuteCommand(object? parameter)
    {
        return _canExecute;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CTestSource : CBindingBase
    {
        private string _value = "initial";

        public string Value
        {
            get
            {
                return _value;
            }

            set
            {
                SetProperty(ref _value, value);
            }
        }
    }
}
