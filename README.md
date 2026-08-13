# Laser Drilling C# Conversion

This solution is the starting skeleton for converting the existing Drilling operation flow into C# / WPF.

## Projects

- `Drilling.UI`: WPF shell, menu controllers, views, command binding.
- `Drilling.Common`: domain models, manager contracts, menu managers, equipment managers, and socket/serial communication.
- `Drilling.File`: CSV file I/O, log file I/O, and file/path-facing implementations.

## Current Scope

- Final menu structure is wired: MAIN, MANUAL, RECIPE, SETTING, ALARM, MONITOR, CALIBRATION, PM, EXIT.
- MAIN prepares an internal Process Plan and builds a 12-head preview from structured pre-script path data.
- Managers access devices and configuration through Common contracts.
- Simulation mode is controlled by `CManager.SetSimul(bool)` and runs through the same interface path as live devices.
- UI placement is controlled by `docs/UI_CONTRACT.md`.
- Menu/controller structure is documented in `docs/MENU_STRUCTURE.md`.
- Communication structure is documented in `docs/COMMUNICATION_STRUCTURE.md`.

## Next Implementation Order

1. Keep `docs/UI_CONTRACT.md` as the source of truth before changing placement.
2. Keep one `Menu*.cs` controller and one XAML view per menu.
3. Define the Scan PC Process Plan builder and validation rules.
4. Expand MAIN cycle state transitions and script generation.
5. Add live equipment protocol methods for IO, motor, laser, chiller, attenuator, and BET.
