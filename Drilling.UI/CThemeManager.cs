using System.Windows;

namespace Drilling.UI;

public enum EN_UI_THEME
{
    Light,
    Dark
}

public static class CThemeManager
{
    private const string LightThemeSource = "Themes/ThemeLight.xaml";
    private const string DarkThemeSource = "Themes/ThemeDark.xaml";

    public static EN_UI_THEME CurrentTheme { get; private set; } = EN_UI_THEME.Dark;

    public static void Apply(EN_UI_THEME theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var source = theme == EN_UI_THEME.Light
            ? LightThemeSource
            : DarkThemeSource;

        var dictionaries = resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var dictionarySource = dictionaries[index].Source?.OriginalString ?? "";
            if (dictionarySource.Contains("ThemeLight.xaml", StringComparison.OrdinalIgnoreCase) ||
                dictionarySource.Contains("ThemeDark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri(source, UriKind.Relative)
        });

        CurrentTheme = theme;
    }

    public static EN_UI_THEME Toggle()
    {
        var nextTheme = CurrentTheme == EN_UI_THEME.Light
            ? EN_UI_THEME.Dark
            : EN_UI_THEME.Light;

        Apply(nextTheme);

        return nextTheme;
    }
}
