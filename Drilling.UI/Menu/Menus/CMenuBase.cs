using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;
using Drilling.UI.Menu;
using System.Windows.Media;

namespace Drilling.UI.Menu.Menus;

public enum EN_MENU
{
    Main,
    Manual,
    Recipe,
    Setting,
    Alarm,
    Monitor,
    Review,
    Correction,
    Pm,
    Exit
}

public abstract class CMenuBase : CBindingBase
{
    public abstract EN_MENU Menu { get; }

    public abstract Task<CScreenViewModel> Build(CancellationToken cancellationToken = default);
}

public sealed record ST_DISPLAY_ITEM(
    string Name,
    string Value,
    string Detail = "")
{
    public Brush ValueBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(string.IsNullOrWhiteSpace(Detail) ? Value : Detail);
        }
    }

    public Brush StateBrush
    {
        get
        {
            return CStatusBrush.ForDisplayState(Value);
        }
    }
}

internal static class CStatusBrush
{
    private static readonly Brush LightPrimaryText = Frozen(0x17, 0x20, 0x2A);
    private static readonly Brush LightRecipe = Frozen(0x8A, 0x3E, 0x5B);
    private static readonly Brush LightOnline = Frozen(0x3E, 0x7A, 0x5A);
    private static readonly Brush LightSimul = Frozen(0x9E, 0x78, 0x2E);
    private static readonly Brush LightOffline = Frozen(0x9B, 0x3D, 0x3D);
    private static readonly Brush LightWait = Frozen(0x9E, 0x78, 0x2E);
    private static readonly Brush LightActive = Frozen(0x46, 0x6F, 0x91);
    private static readonly Brush LightMuted = Frozen(0x6B, 0x77, 0x88);
    private static readonly Brush LightCommandBlue = Frozen(0x46, 0x6F, 0x91);
    private static readonly Brush LightCommandBlueBorder = Frozen(0x34, 0x5D, 0x7F);
    private static readonly Brush LightCommandGreen = Frozen(0x3E, 0x7A, 0x5A);
    private static readonly Brush LightCommandGreenBorder = Frozen(0x32, 0x66, 0x4B);
    private static readonly Brush LightCommandRed = Frozen(0x9B, 0x3D, 0x3D);
    private static readonly Brush LightCommandRedBorder = Frozen(0x7F, 0x2F, 0x2F);
    private static readonly Brush LightCommandDark = Frozen(0x70, 0x80, 0x90);
    private static readonly Brush LightCommandDarkBorder = Frozen(0x5A, 0x66, 0x72);

    private static readonly Brush DarkPrimaryText = Frozen(0xF3, 0xF6, 0xFA);
    private static readonly Brush DarkRecipe = Frozen(0xC0, 0x6B, 0x84);
    private static readonly Brush DarkOnline = Frozen(0x5F, 0x9B, 0x75);
    private static readonly Brush DarkSimul = Frozen(0xC4, 0x9A, 0x35);
    private static readonly Brush DarkOffline = Frozen(0xBF, 0x62, 0x62);
    private static readonly Brush DarkWait = Frozen(0xC4, 0x9A, 0x35);
    private static readonly Brush DarkActive = Frozen(0x86, 0xA8, 0xC1);
    private static readonly Brush DarkMuted = Frozen(0x87, 0x92, 0x9E);
    private static readonly Brush DarkCommandBlue = Frozen(0x3F, 0x66, 0x88);
    private static readonly Brush DarkCommandBlueBorder = Frozen(0x55, 0x7C, 0x9B);
    private static readonly Brush DarkCommandGreen = Frozen(0x3A, 0x76, 0x56);
    private static readonly Brush DarkCommandGreenBorder = Frozen(0x50, 0x91, 0x6C);
    private static readonly Brush DarkCommandRed = Frozen(0x93, 0x40, 0x40);
    private static readonly Brush DarkCommandRedBorder = Frozen(0xAF, 0x58, 0x58);
    private static readonly Brush DarkCommandDark = Frozen(0x2A, 0x32, 0x3B);
    private static readonly Brush DarkCommandDarkBorder = Frozen(0x4B, 0x59, 0x67);

    public static Brush PrimaryText
    {
        get
        {
            return Pick(LightPrimaryText, DarkPrimaryText);
        }
    }

    public static Brush Recipe
    {
        get
        {
            return Pick(LightRecipe, DarkRecipe);
        }
    }

    public static Brush Online
    {
        get
        {
            return Pick(LightOnline, DarkOnline);
        }
    }

    public static Brush Simul
    {
        get
        {
            return Pick(LightSimul, DarkSimul);
        }
    }

    public static Brush Offline
    {
        get
        {
            return Pick(LightOffline, DarkOffline);
        }
    }

    public static Brush Wait
    {
        get
        {
            return Pick(LightWait, DarkWait);
        }
    }

    public static Brush Active
    {
        get
        {
            return Pick(LightActive, DarkActive);
        }
    }

    public static Brush Muted
    {
        get
        {
            return Pick(LightMuted, DarkMuted);
        }
    }

    public static Brush CommandBlue
    {
        get
        {
            return Pick(LightCommandBlue, DarkCommandBlue);
        }
    }

    public static Brush CommandBlueBorder
    {
        get
        {
            return Pick(LightCommandBlueBorder, DarkCommandBlueBorder);
        }
    }

    public static Brush CommandGreen
    {
        get
        {
            return Pick(LightCommandGreen, DarkCommandGreen);
        }
    }

    public static Brush CommandGreenBorder
    {
        get
        {
            return Pick(LightCommandGreenBorder, DarkCommandGreenBorder);
        }
    }

    public static Brush CommandRed
    {
        get
        {
            return Pick(LightCommandRed, DarkCommandRed);
        }
    }

    public static Brush CommandRedBorder
    {
        get
        {
            return Pick(LightCommandRedBorder, DarkCommandRedBorder);
        }
    }

    public static Brush CommandDark
    {
        get
        {
            return Pick(LightCommandDark, DarkCommandDark);
        }
    }

    public static Brush CommandDarkBorder
    {
        get
        {
            return Pick(LightCommandDarkBorder, DarkCommandDarkBorder);
        }
    }

    public static Brush ForHeaderState(string state)
    {
        Brush EvaluateValueSwitch1()
        {
            var switchValue = Normalize(state);
            switch (switchValue)
            {
                case "RECIPE":
                    return Recipe;
                case "ONLINE" or "CLEAR" or "READY" or "OK" or "RUN" or "SAFE" or "DONE":
                    return Online;
                case "SIM" or "SIMUL" or "SIMULATION":
                    return Simul;
                case "OFFLINE" or "OCCUR" or "ERROR" or "NG" or "FAILED" or "FAIL" or "ALARM" or "STOP" or "STOPPED":
                    return Offline;
                case "WAIT" or "WAITING" or "WARN" or "WARNING" or "PENDING":
                    return Wait;
                case "ACTIVE" or "RUNNING":
                    return Active;
                default:
                    return Muted;
            }
        }

        return EvaluateValueSwitch1();
    }

    public static Brush ForDisplayState(string state)
    {
        Brush EvaluateValueSwitch2()
        {
            var switchValue = Normalize(state);
            switch (switchValue)
            {
                case "OK" or "DONE" or "COMPLETE" or "COMPLETED" or "CLEAR" or "READY":
                    return Online;
                case "RUN" or "RUNNING" or "ACTIVE" or "CURRENT":
                    return Active;
                case "WAIT" or "WAITING" or "PENDING" or "WARN" or "WARNING":
                    return Wait;
                case "SKIP" or "SKIPPED":
                    return Muted;
                case "ERROR" or "NG" or "FAILED" or "FAIL" or "ALARM" or "STOP" or "STOPPED":
                    return Offline;
                case "SIM" or "SIMUL" or "SIMULATION":
                    return Simul;
                default:
                    return Muted;
            }
        }

        return EvaluateValueSwitch2();
    }

    public static Brush ForHeadStatus(string status)
    {
        Brush EvaluateValueSwitch3()
        {
            var switchValue = Normalize(status);
            switch (switchValue)
            {
                case "RUN" or "RUNNING" or "ACTIVE":
                    return Active;
                case "WAIT" or "WAITING" or "IDLE":
                    return Wait;
                case "SKIP" or "SKIPPED":
                    return Muted;
                case "ERROR" or "ALARM":
                    return Offline;
                default:
                    return Online;
            }
        }

        return EvaluateValueSwitch3();
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static Brush Pick(Brush lightBrush, Brush darkBrush)
    {
        return Drilling.UI.CThemeManager.CurrentTheme == Drilling.UI.EN_UI_THEME.Light
            ? lightBrush
            : darkBrush;
    }

    public static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed record ST_HEADER_STATUS_ITEM(
    string Name,
    string Value,
    string State,
    bool CanNavigate = false,
    string PageText = "",
    CButtonCommand? PreviousCommand = null,
    CButtonCommand? NextCommand = null,
    CButtonCommand? OpenCommand = null)
{
    public Brush AccentBrush
    {
        get
        {
            return CStatusBrush.ForHeaderState(State);
        }
    }

    public Brush ValueBrush
    {
        get
        {
            return CStatusBrush.ForHeaderState(State);
        }
    }

    public CButtonCommand Previous
    {
        get
        {
            return PreviousCommand ?? CButtonCommand.NoOp;
        }
    }

    public CButtonCommand Next
    {
        get
        {
            return NextCommand ?? CButtonCommand.NoOp;
        }
    }

    public CButtonCommand Open
    {
        get
        {
            return OpenCommand ?? CButtonCommand.NoOp;
        }
    }
}

public sealed record ST_SCREEN_SECTION(
    string Title,
    IReadOnlyList<ST_DISPLAY_ITEM> Items);

public sealed class CScreenViewModel(
    EN_MENU menu,
    string title,
    string subtitle,
    IReadOnlyList<ST_DISPLAY_ITEM> metrics,
    IReadOnlyList<ST_SCREEN_SECTION> sections,
    bool showCycleControls = false,
    CMenuMain? mainOperating = null,
    CMenuManual? manual = null,
    CMenuRecipe? recipe = null,
    CMenuSetting? setting = null,
    CMenuAlarm? alarm = null,
    CMenuMonitor? monitor = null,
    CMenuReview? review = null,
    CMenuCorrection? correction = null,
    CMenuPm? pm = null)
{
    public EN_MENU Menu { get; } = menu;

    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;

    public IReadOnlyList<ST_DISPLAY_ITEM> Metrics { get; } = metrics;

    public IReadOnlyList<ST_SCREEN_SECTION> Sections { get; } = sections;

    public bool ShowCycleControls { get; } = showCycleControls;

    public CMenuMain? MainOperating { get; } = mainOperating;

    public CMenuManual? Manual { get; } = manual;

    public CMenuRecipe? Recipe { get; } = recipe;

    public CMenuSetting? Setting { get; } = setting;

    public CMenuAlarm? Alarm { get; } = alarm;

    public CMenuMonitor? Monitor { get; } = monitor;

    public CMenuReview? Review { get; } = review;

    public CMenuCorrection? Correction { get; } = correction;

    public CMenuPm? Pm { get; } = pm;

    public bool IsMainLayout
    {
        get
        {
            return Menu == EN_MENU.Main;
        }
    }

    public bool IsManualLayout
    {
        get
        {
            return Menu == EN_MENU.Manual;
        }
    }

    public bool IsRecipeLayout
    {
        get
        {
            return Menu == EN_MENU.Recipe;
        }
    }

    public bool IsSettingLayout
    {
        get
        {
            return Menu == EN_MENU.Setting;
        }
    }

    public bool IsAlarmLayout
    {
        get
        {
            return Menu == EN_MENU.Alarm;
        }
    }

    public bool IsMonitorLayout
    {
        get
        {
            return Menu == EN_MENU.Monitor;
        }
    }

    public bool IsReviewLayout
    {
        get
        {
            return Menu == EN_MENU.Review;
        }
    }

    public bool IsCorrectionLayout
    {
        get
        {
            return Menu == EN_MENU.Correction;
        }
    }

    public bool IsPmLayout
    {
        get
        {
            return Menu == EN_MENU.Pm;
        }
    }

    public bool IsGenericLayout
    {
        get
        {
            return !IsMainLayout &&
        !IsManualLayout &&
        !IsRecipeLayout &&
        !IsSettingLayout &&
        !IsAlarmLayout &&
        !IsMonitorLayout &&
        !IsReviewLayout &&
        !IsCorrectionLayout &&
        !IsPmLayout;
        }
    }
}

public sealed class CMenuItem(EN_MENU menu, string name)
{
    public EN_MENU Menu { get; } = menu;

    public string Name { get; } = name;

    public Geometry IconGeometry
    {
        get
        {
            return CMenuIcon.Get(Menu);
        }
    }

    public string Description
    {
        get
        {
            string EvaluateMenuSwitch4()
            {
                var switchValue = Menu;
                switch (switchValue)
                {
                    case EN_MENU.Main:
                        return "Auto";
                    case EN_MENU.Manual:
                        return "Manual";
                    case EN_MENU.Recipe:
                        return "Recipe";
                    case EN_MENU.Setting:
                        return "Setting";
                    case EN_MENU.Alarm:
                        return "Alarm";
                    case EN_MENU.Monitor:
                        return "Monitor";
                    case EN_MENU.Review:
                        return "Review";
                    case EN_MENU.Correction:
                        return "Correction";
                    case EN_MENU.Pm:
                        return "PM Lock";
                    case EN_MENU.Exit:
                        return "Exit";
                    default:
                        return string.Empty;
                }
            }

            return EvaluateMenuSwitch4();
        }
    }
}

internal static class CMenuIcon
{
    private static readonly IReadOnlyDictionary<EN_MENU, Geometry> Icons =
        new Dictionary<EN_MENU, Geometry>
        {
            [EN_MENU.Main] = Icon("M3,11 L12,4 L21,11 M5,10 V21 H10 V15 H14 V21 H19 V10"),
            [EN_MENU.Manual] = Icon("M7,12 V6 M10,12 V4 M13,12 V5 M16,13 V7 M7,12 L5,10 C4,9 3,10 4,12 L6,17 C7,20 9,22 12,22 H14 C17,22 19,19 19,16 V10"),
            [EN_MENU.Recipe] = Icon("M6,4 H18 V20 H6 Z M9,8 H15 M9,12 H15 M9,16 H13"),
            [EN_MENU.Setting] = Icon("M12,8 C14.2,8 16,9.8 16,12 C16,14.2 14.2,16 12,16 C9.8,16 8,14.2 8,12 C8,9.8 9.8,8 12,8 M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M19.1,4.9 L17,7 M7,17 L4.9,19.1"),
            [EN_MENU.Alarm] = Icon("M8,18 H16 M10,20 H14 M6,17 H18 L16,15 V10 C16,7.8 14.2,6 12,6 C9.8,6 8,7.8 8,10 V15 Z M10,6 C10,4.9 10.9,4 12,4 C13.1,4 14,4.9 14,6"),
            [EN_MENU.Monitor] = Icon("M4,5 H20 V16 H4 Z M9,20 H15 M12,16 V20 M7,11 H10 L11.5,8 L14,14 L15,11 H17"),
            [EN_MENU.Review] = Icon("M11,5 C14.3,5 17,7.7 17,11 C17,14.3 14.3,17 11,17 C7.7,17 5,14.3 5,11 C5,7.7 7.7,5 11,5 M15.5,15.5 L20,20 M11,7 V15 M7,11 H15"),
            [EN_MENU.Correction] = Icon("M12,4 V8 M12,16 V20 M4,12 H8 M16,12 H20 M8.5,8.5 L6,6 M15.5,15.5 L18,18 M15.5,8.5 L18,6 M8.5,15.5 L6,18 M10,10 H14 V14 H10 Z"),
            [EN_MENU.Pm] = Icon("M15,4 C17,3 19,4 20,5 L17,8 L19,10 L16,13 L14,11 L7,18 C6,19 5,19 4,18 C3,17 3,16 4,15 L11,8 L9,6 L12,3 Z"),
            [EN_MENU.Exit] = Icon("M10,5 H5 V19 H10 M13,8 L18,12 L13,16 M18,12 H8")
        };

    public static Geometry Get(EN_MENU menu)
    {
        return Icons.TryGetValue(menu, out var geometry)
            ? geometry
            : Geometry.Empty;
    }

    private static Geometry Icon(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
