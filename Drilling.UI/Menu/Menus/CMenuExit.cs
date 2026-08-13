using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuExit : IMenu
{
    public EN_MENU Menu => EN_MENU.Exit;

    public Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
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

        return Task.FromResult(screen);
    }
}



