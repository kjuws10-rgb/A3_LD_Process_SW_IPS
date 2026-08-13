using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuExit : CMenuBase
{
    public override EN_MENU Menu
    {
        get
        {
            return EN_MENU.Exit;
        }
    }

    public override CScreenViewModel Build(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var screen = new CScreenViewModel(
            EN_MENU.Exit,
            "EXIT",
            "Application shutdown entry point.",
            [
                new("State", "Ready")
            ],
            [
                new("Exit", [
                    new("Close Application", "Pending")
                ])
            ]);

        return (screen);
    }
}
