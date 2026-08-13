using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuPm(
    Func<ST_PM_LOCK_STATUS> lockStatusProvider,
    Action enterLock) : IMenu
{
    public EN_MENU Menu => EN_MENU.Pm;

    public string StartTime { get; private set; } = "";

    public IReadOnlyList<ST_DISPLAY_ITEM> LockItems { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> BlockedItems { get; private set; } = [];

    public Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var current = lockStatusProvider();

        if (!current.IsLocked)
        {
            enterLock();
            current = lockStatusProvider();
        }

        var startTime = current.LockedAt?.ToString("HH:mm:ss") ?? DateTime.Now.ToString("HH:mm:ss");
        Apply(
            startTime,
            [
                new("Lock State", current.IsLocked ? "LOCKED" : "UNLOCKED"),
                new("Locked At", startTime),
                new("Exit", "Password required")
            ],
            [
                new("Equipment Operation", "Blocked"),
                new("Manual Command", "Blocked"),
                new("Cycle Start", "Blocked")
            ]);

        var screen = new CScreenViewModel(
            EN_MENU.Pm,
            "PM",
            "Operation lock screen. Password is required to exit PM.",
            [
                new("Lock", current.IsLocked ? "LOCKED" : "UNLOCKED"),
                new("Locked At", current.LockedAt?.ToString("HH:mm:ss") ?? "-")
            ],
            [
                new("Operation", [
                    new("Equipment Control", "Blocked"),
                    new("Exit PM", "Password required")
                ])
            ],
            pm: this);

        return Task.FromResult(screen);
    }

    private void Apply(
        string startTime,
        IReadOnlyList<ST_DISPLAY_ITEM> lockItems,
        IReadOnlyList<ST_DISPLAY_ITEM> blockedItems)
    {
        StartTime = startTime;
        LockItems = lockItems;
        BlockedItems = blockedItems;
    }
}



