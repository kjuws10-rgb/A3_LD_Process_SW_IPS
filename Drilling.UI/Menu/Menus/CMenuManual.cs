using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Drilling.Common.Managers;
using Drilling.Common.Station;
using Drilling.UI.Menu;
using Drilling.UI.Popup;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuManual : CMenuBase
{
    private const int ManualTaskIndex = 1;

    private readonly CManager _manager;
    private readonly CManualScanFileBase _scanFile;
    private readonly CAutomationScriptFileBase _scriptFile;
    private readonly Func<int> _selectedHeadNoProvider;
    private readonly Func<string> _selectedSettingNameProvider;
    private readonly Action<string> _selectedSettingNameSetter;
    private readonly Action<string> _setStatusMessage;
    private readonly Action _refreshShellStatus;
    private readonly Action _refreshCurrentScreen;

    private string _targetGx = "";
    private string _targetGy = "";
    private int _lastBuiltHeadNo;
    private string _laserState = "OFF";
    private string _centerState = "OFF";
    private string _lastCommand = "-";
    private string _lastResult = "Ready";
    private string _lastLoadedSettingName = "";
    private string _shapeSize = "10.000";
    private string _shapeOffsetX = "0.000";
    private string _shapeOffsetY = "0.000";
    private string _shapeDirection = "CW";
    private string _shapeName = "Circle";
    private string _gridRowLines = "5";
    private string _gridColLines = "5";
    private string _laserOnMode = "TIME";
    private string _laserOnTimeMsec = "1000";
    private string _laserShotCount = "1";
    private string _laserActionPower = "1.00";
    private string _laserActionFrequency = "20.000";
    private bool _isScannerWorkspace = true;
    private ImageSource? _visionImage;
    private string _visionCaptureStatus = "Ready";
    private string _visionCaptureTime = "-";

    public CMenuManual(
        CManager manager,
        CManualScanFileBase scanFile,
        CAutomationScriptFileBase scriptFile,
        Func<int> selectedHeadNoProvider,
        Func<string> selectedSettingNameProvider,
        Action<string> selectedSettingNameSetter,
        CButtonCommand selectHeadCommand,
        Action<string> setStatusMessage,
        Action refreshShellStatus,
        Action refreshCurrentScreen)
    {
        _manager = manager;
        _scanFile = scanFile;
        _scriptFile = scriptFile;
        _selectedHeadNoProvider = selectedHeadNoProvider;
        _selectedSettingNameProvider = selectedSettingNameProvider;
        _selectedSettingNameSetter = selectedSettingNameSetter;
        _setStatusMessage = setStatusMessage;
        _refreshShellStatus = refreshShellStatus;
        _refreshCurrentScreen = refreshCurrentScreen;

        SelectHeadCommand = selectHeadCommand;

        void HandleSelectSettingCommand1(object? parameter)
        {
            SelectSetting(parameter);
        }

        SelectSettingCommand = new CButtonCommand(HandleSelectSettingCommand1);

        void HandleCreateCommand2(object? _)
        {
            Create();
        }

        CreateCommand = new CButtonCommand(HandleCreateCommand2);

        void HandleDeleteCommand3(object? _)
        {
            Delete();
        }

        DeleteCommand = new CButtonCommand(HandleDeleteCommand3);

        void HandleRenameCommand4(object? _)
        {
            Rename();
        }

        RenameCommand = new CButtonCommand(HandleRenameCommand4);

        void HandleSaveCommand5(object? _)
        {
            Save();
        }

        SaveCommand = new CButtonCommand(HandleSaveCommand5);

        void HandleCenterMoveCommand6(object? _)
        {
            CenterMove();
        }

        CenterMoveCommand = new CButtonCommand(HandleCenterMoveCommand6);

        void HandlePositionMoveCommand7(object? _)
        {
            PositionMove();
        }

        PositionMoveCommand = new CButtonCommand(HandlePositionMoveCommand7);

        void HandleMoveStopCommand8(object? _)
        {
            MoveStop();
        }

        MoveStopCommand = new CButtonCommand(HandleMoveStopCommand8);

        void HandleSelectShapeCommand9(object? parameter)
        {
            SelectShape(parameter);
        }

        SelectShapeCommand = new CButtonCommand(HandleSelectShapeCommand9);

        void HandleShapeStartCommand10(object? _)
        {
            ShapeStart();
        }

        ShapeStartCommand = new CButtonCommand(HandleShapeStartCommand10);

        void HandleShapeStopCommand11(object? _)
        {
            ShapeStop();
        }

        ShapeStopCommand = new CButtonCommand(HandleShapeStopCommand11);

        void HandleLaserOnCommand12(object? _)
        {
            LaserOn();
        }

        LaserOnCommand = new CButtonCommand(HandleLaserOnCommand12);

        void HandleLaserOffCommand13(object? _)
        {
            LaserOff();
        }

        LaserOffCommand = new CButtonCommand(HandleLaserOffCommand13);

        void HandleCenterOnCommand14(object? _)
        {
            CenterOn();
        }

        CenterOnCommand = new CButtonCommand(HandleCenterOnCommand14);
        SelectManualWorkspaceCommand = new CButtonCommand(SelectManualWorkspace);
        StageMoveCommand = new CButtonCommand(StageMove);
        void HandleStageHomeCommand15(object? _)
        {
            StageHome();
        }

        StageHomeCommand = new CButtonCommand(HandleStageHomeCommand15);
        void HandleStageStopCommand16(object? _)
        {
            StageStop();
        }

        StageStopCommand = new CButtonCommand(HandleStageStopCommand16);
        VisionShotCommand = new CButtonCommand(VisionShot);

        StageAxes =
        [
            new("Y", "mm")
        ];
    }

    public override EN_MENU Menu
    {
        get
        {
            return EN_MENU.Manual;
        }
    }

    public IReadOnlyList<ST_DISPLAY_ITEM> ManualSettings { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> SelectedHeadItems { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> PositionMoveItems { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> ShapeScanItems { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> CommandStateItems { get; private set; } = [];

    public string SelectedHead { get; private set; } = "";

    public string LoadedSettingName { get; private set; } = "";

    public string LoadedSettingPath { get; private set; } = "";

    public string TargetGx
    {
        get
        {
            return _targetGx;
        }

        set
        {
            SetProperty(ref _targetGx, value);
        }
    }

    public string TargetGy
    {
        get
        {
            return _targetGy;
        }

        set
        {
            SetProperty(ref _targetGy, value);
        }
    }

    public string ShapeSize
    {
        get
        {
            return _shapeSize;
        }

        set
        {
            SetProperty(ref _shapeSize, value);
        }
    }

    public string ShapeOffsetX
    {
        get
        {
            return _shapeOffsetX;
        }

        set
        {
            SetProperty(ref _shapeOffsetX, value);
        }
    }

    public string ShapeOffsetY
    {
        get
        {
            return _shapeOffsetY;
        }

        set
        {
            SetProperty(ref _shapeOffsetY, value);
        }
    }

    public string ShapeDirection
    {
        get
        {
            return _shapeDirection;
        }

        set
        {
            SetProperty(ref _shapeDirection, value);
        }
    }

    public string ShapeName
    {
        get
        {
            return _shapeName;
        }

        set
        {
            SetProperty(ref _shapeName, NormalizeShapeName(value));
        }
    }

    public string GridRowLines
    {
        get
        {
            return _gridRowLines;
        }

        set
        {
            SetProperty(ref _gridRowLines, value);
        }
    }

    public string GridColLines
    {
        get
        {
            return _gridColLines;
        }

        set
        {
            SetProperty(ref _gridColLines, value);
        }
    }

    public string LaserOnMode
    {
        get
        {
            return _laserOnMode;
        }

        set
        {
            var normalizedValue = NormalizeLaserOnMode(value);
            if (SetProperty(ref _laserOnMode, normalizedValue))
            {
                OnPropertyChanged(nameof(IsLaserTimeMode));
                OnPropertyChanged(nameof(IsLaserCountMode));
                RefreshCommandStateRows();
            }
        }
    }

    public bool IsLaserTimeMode
    {
        get
        {
            return IsLaserTimeModeValue(LaserOnMode);
        }
    }

    public bool IsLaserCountMode
    {
        get
        {
            return !IsLaserTimeMode;
        }
    }

    public string LaserOnTimeMsec
    {
        get
        {
            return _laserOnTimeMsec;
        }

        set
        {
            SetProperty(ref _laserOnTimeMsec, value);
        }
    }

    public string LaserShotCount
    {
        get
        {
            return _laserShotCount;
        }

        set
        {
            SetProperty(ref _laserShotCount, value);
        }
    }

    public IReadOnlyList<ST_MANUAL_HEAD_CARD> HeadCards { get; private set; } = [];

    public IReadOnlyList<ST_MANUAL_SETTING_FILE> SettingFiles { get; private set; } = [];

    public IReadOnlyList<ST_MANUAL_PARAMETER> SettingParameters { get; private set; } = [];

    public IReadOnlyList<ST_MANUAL_COMMAND_STATE> CommandStateRows { get; private set; } = [];

    public IReadOnlyList<ST_MANUAL_STAGE_AXIS> StageAxes { get; }

    public bool IsScannerWorkspace
    {
        get
        {
            return _isScannerWorkspace;
        }

        private set
        {
            if (SetProperty(ref _isScannerWorkspace, value))
            {
                OnPropertyChanged(nameof(IsStageVisionWorkspace));
            }
        }
    }

    public bool IsStageVisionWorkspace
    {
        get
        {
            return !IsScannerWorkspace;
        }
    }

    public ImageSource? VisionImage
    {
        get
        {
            return _visionImage;
        }

        private set
        {
            if (SetProperty(ref _visionImage, value))
            {
                OnPropertyChanged(nameof(HasVisionImage));
            }
        }
    }

    public bool HasVisionImage
    {
        get
        {
            return VisionImage is not null;
        }
    }

    public string VisionCaptureStatus
    {
        get
        {
            return _visionCaptureStatus;
        }

        private set
        {
            SetProperty(ref _visionCaptureStatus, value);
        }
    }

    public string VisionCaptureTime
    {
        get
        {
            return _visionCaptureTime;
        }

        private set
        {
            SetProperty(ref _visionCaptureTime, value);
        }
    }

    public CButtonCommand SelectHeadCommand { get; }

    public CButtonCommand SelectSettingCommand { get; }

    public CButtonCommand CreateCommand { get; }

    public CButtonCommand DeleteCommand { get; }

    public CButtonCommand RenameCommand { get; }

    public CButtonCommand SaveCommand { get; }

    public CButtonCommand CenterMoveCommand { get; }

    public CButtonCommand PositionMoveCommand { get; }

    public CButtonCommand MoveStopCommand { get; }

    public CButtonCommand SelectShapeCommand { get; }

    public CButtonCommand ShapeStartCommand { get; }

    public CButtonCommand ShapeStopCommand { get; }

    public CButtonCommand LaserOnCommand { get; }

    public CButtonCommand LaserOffCommand { get; }

    public CButtonCommand CenterOnCommand { get; }

    public CButtonCommand SelectManualWorkspaceCommand { get; }

    public CButtonCommand StageMoveCommand { get; }

    public CButtonCommand StageHomeCommand { get; }

    public CButtonCommand StageStopCommand { get; }

    public CButtonCommand VisionShotCommand { get; }

    public override CScreenViewModel Build(CancellationToken cancellationToken = default)
    {
        var settingNames = _scanFile.List(cancellationToken);
        var formItems = _scanFile.LoadForm(cancellationToken);
        var selectedSettingName = ResolveSelectedSettingName(settingNames, _selectedSettingNameProvider());
        var settings = _scanFile.Load(selectedSettingName, cancellationToken);
        var selectedHeadNo = Math.Clamp(_selectedHeadNoProvider(), 1, 8);
        var headCards = BuildHeadCards(selectedHeadNo);
        bool MatchHead17(ST_MANUAL_HEAD_CARD head)
        {
            return head.IsSelected;
        }

        var selectedHead = headCards.First(MatchHead17);

        if (_lastBuiltHeadNo != selectedHeadNo ||
            string.IsNullOrWhiteSpace(TargetGx) ||
            string.IsNullOrWhiteSpace(TargetGy))
        {
            TargetGx = selectedHead.Gx;
            TargetGy = selectedHead.Gy;
            _lastBuiltHeadNo = selectedHeadNo;
        }

        if (!_lastLoadedSettingName.Equals(selectedSettingName, StringComparison.OrdinalIgnoreCase))
        {
            ShapeSize = FormatDouble(settings.ShapeSize, 3);
            ShapeOffsetX = FormatDouble(settings.OffsetX, 3);
            ShapeOffsetY = FormatDouble(settings.OffsetY, 3);
            ShapeDirection = string.IsNullOrWhiteSpace(settings.Direction) ? "CW" : settings.Direction;
            ShapeName = settings.ShapeName;
            GridRowLines = settings.GridRowLines.ToString(CultureInfo.InvariantCulture);
            GridColLines = settings.GridColLines.ToString(CultureInfo.InvariantCulture);
            _lastLoadedSettingName = selectedSettingName;
        }

        var selectedHeadItems = new ST_DISPLAY_ITEM[]
        {
            new("Head", selectedHead.HeadName),
            new("GX Position", selectedHead.Gx, "mm"),
            new("GY Position", selectedHead.Gy, "mm"),
            new("Servo", "ON"),
            new("Motion", selectedHead.State)
        };

        Apply(
            BuildManualSettings(settings),
            selectedHeadItems,
            [
                new("Center Move", "Ready"),
                new("Position Move", "GX/GY Target"),
                new("Move Stop", "Ready"),
                new("GX Target", TargetGx, "mm"),
                new("GY Target", TargetGy, "mm")
            ],
            [
                new("Shape Size", ShapeSize, "mm"),
                new("Offset X", ShapeOffsetX, "mm"),
                new("Offset Y", ShapeOffsetY, "mm"),
                new("Direction", ShapeDirection),
                new("Shape", ShapeName),
                new("Grid Row Lines", GridRowLines, "ea"),
                new("Grid Col Lines", GridColLines, "ea"),
                new("Start", "Ready"),
                new("Stop", "Ready")
            ],
            [
                new("Laser", _laserState),
                new("CENTER", _centerState),
                new("Last Command", _lastCommand),
                new("Result", _lastResult)
            ],
            selectedHead.HeadName,
            selectedSettingName,
            $@"Config\Manual\{selectedSettingName}",
            headCards,
            BuildSettingFiles(settingNames, selectedSettingName),
            BuildSettingParameters(settings, formItems),
            BuildCommandStateRows(selectedHead));

        return new CScreenViewModel(
            EN_MENU.Manual,
            "MANUAL / CONTROL",
            "Scanner, stage and vision manual operation.",
            [
                new("Selected Head", selectedHead.HeadName),
                new("Mode", "Manual"),
                new("Laser", _laserState)
            ],
            [
                new("Manual Setting", BuildManualSettings(settings)),
                new("Position Move", [
                    new("Center Move", "Ready"),
                    new("Position Move", "GX/GY target input"),
                    new("Move Stop", "Ready")
                ])
            ],
            manual: this);
    }

    private void SelectManualWorkspace(object? parameter)
    {
        IsScannerWorkspace = !string.Equals(
            parameter?.ToString(),
            "STAGE_VISION",
            StringComparison.OrdinalIgnoreCase);
    }

    private void StageMove(object? parameter)
    {
        if (parameter is not ST_MANUAL_STAGE_AXIS axis)
        {
            return;
        }

        if (!double.TryParse(
                axis.TargetPosition,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var targetPosition))
        {
            var message = $"Stage {axis.DisplayAxis} target is not a valid number: {axis.TargetPosition}";
            _setStatusMessage(message);
            ShowManualWarning(message);
            return;
        }

        _setStatusMessage(
            $"Stage {axis.DisplayAxis} move requested (UI only). Target={targetPosition:0.###} {axis.Unit}.");
        _refreshShellStatus();
    }

    private void StageStop()
    {
        _setStatusMessage("Stage Y stop requested (UI only).");
        _refreshShellStatus();
    }

    private void StageHome()
    {
        _setStatusMessage("Stage Y home requested (UI only).");
        _refreshShellStatus();
    }

    private void VisionShot(object? _)
    {
        VisionCaptureTime = "-";
        VisionCaptureStatus = "Capturing";
        _setStatusMessage("Vision shot requested. Camera capture interface is not connected yet.");
        _refreshShellStatus();
    }

    private void SelectSetting(object? parameter)
    {
        var settingName = GetManualSettingNameFromParameter(parameter);

        if (string.IsNullOrWhiteSpace(settingName))
        {
            return;
        }

        _selectedSettingNameSetter(settingName);
        _setStatusMessage($"Manual setting {settingName} selected.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void Create()
    {
        var settingNames = _scanFile.List();
        string HandleNewSettingName18(string value)
        {
            return ValidateManualSettingName(NormalizeManualSettingNameInput(value), settingNames);
        }

        var newSettingName = ShowManualSettingNameDialog(
            "Create Manual Setting",
            "Enter the new manual scan setting name.",
            "",
HandleNewSettingName18);

        if (newSettingName is null)
        {
            _setStatusMessage("Manual setting create canceled.");
            return;
        }

        var validationMessage = ValidateManualSettingName(newSettingName, settingNames);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _setStatusMessage(validationMessage);
            return;
        }

        if (!TryReadManualParamFromScreen(out var settings, out var errorMessage))
        {
            _setStatusMessage(errorMessage);
            ShowManualWarning(errorMessage);
            return;
        }

        if (!TrySaveSetting(newSettingName, settings))
        {
            return;
        }

        _selectedSettingNameSetter(newSettingName);
        _setStatusMessage($"Manual setting {newSettingName} created and CSV verified.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(LoadedSettingName))
        {
            _setStatusMessage("Manual setting save skipped. No manual setting is selected.");
            return;
        }

        if (!TryReadManualParamFromScreen(out var settings, out var errorMessage))
        {
            _setStatusMessage(errorMessage);
            ShowManualWarning(errorMessage);
            return;
        }

        if (!TrySaveSetting(LoadedSettingName, settings))
        {
            return;
        }

        _selectedSettingNameSetter(LoadedSettingName);
        _setStatusMessage($"Manual setting {LoadedSettingName} saved and CSV verified.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void Rename()
    {
        var oldSettingName = GetManualSettingNameFromParameter(LoadedSettingName);

        if (string.IsNullOrWhiteSpace(oldSettingName))
        {
            _setStatusMessage("Manual setting rename skipped. No manual setting is selected.");
            return;
        }

        var settingNames = _scanFile.List();
        string HandleNewSettingName19(string value)
        {
            return ValidateManualSettingName(NormalizeManualSettingNameInput(value), settingNames, oldSettingName);
        }

        var newSettingName = ShowManualSettingNameDialog(
            "Rename Manual Setting",
            "Enter the new manual scan setting name.",
            Path.GetFileNameWithoutExtension(oldSettingName),
HandleNewSettingName19);

        if (newSettingName is null)
        {
            _setStatusMessage("Manual setting rename canceled.");
            return;
        }

        if (newSettingName.Equals(oldSettingName, StringComparison.OrdinalIgnoreCase))
        {
            _setStatusMessage("Manual setting rename skipped. Name was not changed.");
            return;
        }

        var validationMessage = ValidateManualSettingName(newSettingName, settingNames, oldSettingName);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _setStatusMessage(validationMessage);
            return;
        }

        if (!TryReadManualParamFromScreen(out var settings, out var errorMessage))
        {
            _setStatusMessage(errorMessage);
            ShowManualWarning(errorMessage);
            return;
        }

        if (!TrySaveSetting(oldSettingName, settings))
        {
            return;
        }

        try
        {
            _scanFile.Rename(oldSettingName, newSettingName);
        }
        catch (IOException exception)
        {
            _setStatusMessage($"Manual setting rename blocked. {exception.Message}");
            return;
        }

        _selectedSettingNameSetter(newSettingName);
        _setStatusMessage($"Manual setting {oldSettingName} renamed to {newSettingName}.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private bool TrySaveSetting(
        string settingName,
        ST_MANUAL_SCAN_PARAM settings)
    {
        try
        {
            _scanFile.Save(settingName, settings);
            return true;
        }
        catch (InvalidDataException exception)
        {
            _setStatusMessage(exception.Message);
            ShowManualWarning(exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            _setStatusMessage($"Manual setting save blocked. {exception.Message}");
            return false;
        }
    }

    private void Delete()
    {
        var settingName = GetManualSettingNameFromParameter(LoadedSettingName);

        if (string.IsNullOrWhiteSpace(settingName))
        {
            _setStatusMessage("Manual setting delete skipped. No manual setting is selected.");
            return;
        }

        if (!ConfirmManualSettingDelete(settingName))
        {
            _setStatusMessage($"Manual setting {settingName} delete canceled.");
            return;
        }

        _scanFile.Delete(settingName);

        var remainingSettings = _scanFile.List();
        _selectedSettingNameSetter(remainingSettings.FirstOrDefault() ?? "CIRCLE_TEST.scan");
        _setStatusMessage($"Manual setting {settingName} deleted.");
        _refreshShellStatus();
        _refreshCurrentScreen();
    }

    private void CenterMove()
    {
        void RunManualScriptScriptCallback20(CAutomation1ScriptBase script, ST_MANUAL_SCAN_PARAM _)
        {
            script.Jump(0.0, 0.0);
            script.WaitMoveDone();
        }
        RunManualScript(
            "CENTER_MOVE",
RunManualScriptScriptCallback20);
    }

    private void PositionMove()
    {
        void RunManualScriptScriptCallback21(CAutomation1ScriptBase script, ST_MANUAL_SCAN_PARAM _)
        {
            var gx = ReadRequiredDouble(TargetGx, "GX Target");
            var gy = ReadRequiredDouble(TargetGy, "GY Target");
            script.Jump(gx, gy);
            script.WaitMoveDone();
        }
        RunManualScript(
            "POSITION_MOVE",
RunManualScriptScriptCallback21);
    }

    private void MoveStop()
    {
        StopManualTask("MOVE_STOP");
    }

    private void SelectShape(object? parameter)
    {
        var shapeName = NormalizeShapeName(parameter?.ToString() ?? "");
        ShapeName = shapeName;
        _lastCommand = "SELECT_SHAPE";
        _lastResult = shapeName;
        _setStatusMessage($"Manual shape selected: {shapeName}.");
        return;
    }

    private void ShapeStart()
    {
        var headNo = Math.Clamp(_selectedHeadNoProvider(), 1, 8);
        var scriptName = BuildShapeScanScriptName(headNo, ShapeName);
        void RunManualScriptScriptCallback22(CAutomation1ScriptBase script, ST_MANUAL_SCAN_PARAM settings)
        {
            AppendShape(script, settings);
            script.GCodeMove(0.0, 0.0);
        }
        RunManualScript(
            scriptName,
RunManualScriptScriptCallback22,
            ApplyManualFigureScanSetup,
            scriptName);
    }

    private void ShapeStop()
    {
        StopManualTask("SHAPE_STOP");
    }

    private void LaserOn()
    {
        void RunManualScriptScriptCallback23(CAutomation1ScriptBase script, ST_MANUAL_SCAN_PARAM settings)
        {
            var laserActionSettings = ReadLaserActionSettings(settings);
            var moveDelaySeconds = ReadLaserMoveDelaySeconds(laserActionSettings);
            script.LaserOn();
            script.SetMoveDelay(moveDelaySeconds);
            script.LaserOff();
        }
        RunManualScript(
            "LASER_ON",
RunManualScriptScriptCallback23,
            ApplyLaserActionScriptSetup);

        if (!_lastResult.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            _laserState = "OFF";
        }
    }

    private void LaserOff()
    {
        void RunManualScriptScriptCallback24(CAutomation1ScriptBase script, ST_MANUAL_SCAN_PARAM _)
        {
            script.LaserOff();
        }

        RunManualScript(
            "LASER_OFF",
RunManualScriptScriptCallback24,
            ApplyManualLaserOffScriptSetup);

        if (!_lastResult.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            _laserState = "OFF";
            _centerState = "OFF";
        }
    }

    private void CenterOn()
    {
        void RunManualScriptScriptCallback25(CAutomation1ScriptBase script, ST_MANUAL_SCAN_PARAM settings)
        {
            var laserActionSettings = ReadLaserActionSettings(settings);
            var moveDelaySeconds = ReadLaserMoveDelaySeconds(laserActionSettings);
            script.Jump(0.0, 0.0);
            script.WaitMoveDone();
            script.LaserOn();
            script.SetMoveDelay(moveDelaySeconds);
            script.LaserOff();
        }
        RunManualScript(
            "CENTER_ON",
RunManualScriptScriptCallback25,
            ApplyLaserActionScriptSetup);

        if (!_lastResult.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            _laserState = "OFF";
            _centerState = "OFF";
        }
    }

    private void RunManualScript(
        string commandName,
        Action<CAutomation1ScriptBase, ST_MANUAL_SCAN_PARAM> buildScript,
        Action<CAutomation1ScriptBase, ST_MANUAL_SCAN_PARAM>? setupScript = null,
        string? scriptFileName = null,
        CancellationToken cancellationToken = default)
    {
        _lastCommand = commandName;
        _lastResult = "Building";

        try
        {
            var settings = ReadManualParamFromScreen();
            var headNo = Math.Clamp(_selectedHeadNoProvider(), 1, 8);
            var fileName = string.IsNullOrWhiteSpace(scriptFileName)
                ? $"MANUAL_H{headNo:00}_{NormalizeScriptName(commandName)}.ascript"
                : $"{NormalizeScriptName(scriptFileName)}.ascript";
            var script = _scriptFile.Create(fileName);

            script.SetDeviceNo(headNo - 1);
            script.SetAxis("GX", "GY");
            AppendManualSettingComment(script, settings);
            script.Start($"Manual H{headNo:00} {commandName}");
            (setupScript ?? ApplyManualScriptSetup)(script, settings);
            buildScript(script, settings);
            script.End();

            var savedScript = script.Save(cancellationToken);
            var uploadResponse = _manager.automation.UploadScript(
                savedScript.FilePath,
                savedScript.FileName,
                cancellationToken: cancellationToken);
            EnsureAutomationResponse(uploadResponse, $"{commandName} upload");

            var runResponse = _manager.automation.RunScript(
                savedScript.FileName,
                ManualTaskIndex,
                cancellationToken: cancellationToken);
            EnsureAutomationResponse(runResponse, $"{commandName} run");

            _lastResult = $"Running {savedScript.FileName}";
            _setStatusMessage($"{commandName} script uploaded and started: {savedScript.FileName}");
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException or TimeoutException or KeyNotFoundException)
        {
            _lastResult = $"ERROR: {exception.Message}";
            _setStatusMessage($"{commandName} failed. {exception.Message}");
            if (exception is InvalidDataException)
            {
                ShowManualWarning(exception.Message);
            }
        }

        RefreshCommandStateRows();
        _refreshShellStatus();
    }

    private void StopManualTask(
        string commandName,
        CancellationToken cancellationToken = default)
    {
        _lastCommand = commandName;
        _lastResult = "Stopping";

        try
        {
            var response = _manager.automation.StopTask(
                ManualTaskIndex,
                cancellationToken: cancellationToken);
            EnsureAutomationResponse(response, $"{commandName} stop");

            _laserState = "OFF";
            _centerState = "OFF";
            _lastResult = "Stopped";
            _setStatusMessage($"{commandName} command sent.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or KeyNotFoundException)
        {
            _lastResult = $"ERROR: {exception.Message}";
            _setStatusMessage($"{commandName} failed. {exception.Message}");
        }

        RefreshCommandStateRows();
        _refreshShellStatus();
    }

    private void RefreshCommandStateRows()
    {
        bool MatchHead26(ST_MANUAL_HEAD_CARD head)
        {
            return head.IsSelected;
        }

        var selectedHead = HeadCards.FirstOrDefault(MatchHead26);

        if (selectedHead is null)
        {
            var selectedHeadNo = Math.Clamp(_selectedHeadNoProvider(), 1, 8);
            bool MatchHead27(ST_MANUAL_HEAD_CARD head)
            {
                return head.IsSelected;
            }

            selectedHead = BuildHeadCards(selectedHeadNo).First(MatchHead27);
        }

        var settings = TryReadManualParamFromScreen(out var currentSettings, out _)
            ? currentSettings
            : CreateFallbackManualParam();

        CommandStateItems =
        [
            new("Laser", _laserState),
            new("CENTER", _centerState),
            new("Last Command", _lastCommand),
            new("Result", _lastResult)
        ];
        CommandStateRows = BuildCommandStateRows(selectedHead);
        OnPropertyChanged(nameof(CommandStateItems));
        OnPropertyChanged(nameof(CommandStateRows));
    }

    private static void ApplyManualScriptSetup(
        CAutomation1ScriptBase script,
        ST_MANUAL_SCAN_PARAM settings)
    {
        script.DefaultSetting(resetPso: false);
        script.SetFrequency(settings.LaserFrequency);
        script.SetLaserDelay(settings.LaserOnDelay, settings.LaserOffDelay);
        script.SetLaserPower(settings.LaserPower);
        script.SetJumpSpeed(settings.JumpSpeed);
        script.SetMarkSpeed(settings.MarkSpeed);
    }

    private void ApplyLaserActionScriptSetup(
        CAutomation1ScriptBase script,
        ST_MANUAL_SCAN_PARAM settings)
    {
        ApplyManualScriptSetup(script, ReadLaserActionSettings(settings));
    }

    private static void ApplyManualLaserOffScriptSetup(
        CAutomation1ScriptBase script,
        ST_MANUAL_SCAN_PARAM _)
    {
        script.DefaultSetting(resetPso: false);
    }

    private static void ApplyManualFigureScanSetup(
        CAutomation1ScriptBase script,
        ST_MANUAL_SCAN_PARAM settings)
    {
        script.DefaultFigureScanSetting();
        script.SetJumpSpeed(settings.JumpSpeed);
        script.SetMarkSpeed(settings.MarkSpeed);
        script.SetMoveDelay(0.0);
        script.SetScannerAcc(50000.0);
        script.SetLaserDelay(settings.LaserOnDelay, settings.LaserOffDelay);
        script.SetFrequency(settings.LaserFrequency);
        script.SetLaserPowerNoDelay(settings.LaserPower);
        script.SetLaserMode(0);
        script.LaserAuto();
    }

    private static void AppendManualSettingComment(
        CAutomation1ScriptBase script,
        ST_MANUAL_SCAN_PARAM settings)
    {
        script.AddLine($"// ManualSetting LaserPower={FormatDouble(settings.LaserPower, 3)} W");
        script.AddLine($"// ManualSetting JumpSpeed={FormatDouble(settings.JumpSpeed, 3)} m/sec");
        script.AddLine($"// ManualSetting MarkSpeed={FormatDouble(settings.MarkSpeed, 3)} m/sec");
        script.AddLine($"// ManualSetting Frequency={FormatDouble(settings.LaserFrequency, 3)} kHz");
        script.AddLine($"// ManualSetting Delay={FormatDouble(settings.LaserOnDelay, 3)}/{FormatDouble(settings.LaserOffDelay, 3)} usec");
        script.AddLine($"// ManualSetting Grid={settings.GridRowLines}x{settings.GridColLines}");
    }

    private void AppendShape(
        CAutomation1ScriptBase script,
        ST_MANUAL_SCAN_PARAM settings)
    {
        var shapeName = NormalizeShapeName(settings.ShapeName);
        var cx = settings.OffsetX;
        var cy = settings.OffsetY;
        var half = Math.Abs(settings.ShapeSize) / 2.0;

        switch (shapeName.ToUpperInvariant())
        {
            case "DOT":
                var moveDelaySeconds = ReadLaserMoveDelaySeconds(settings);
                script.Jump(cx, cy);
                script.LaserOn();
                script.SetMoveDelay(moveDelaySeconds);
                script.LaserOff();
                break;

            case "CROSS":
                DrawLine(script, cx - half, cy, cx + half, cy);
                DrawLine(script, cx, cy - half, cx, cy + half);
                break;

            case "RECT":
                DrawRect(script, cx, cy, half, settings.Direction);
                break;

            case "GRID":
                DrawGrid(script, cx, cy, half, settings.GridRowLines, settings.GridColLines);
                break;

            case "H-LINE":
            case "HLINE":
                DrawLine(script, cx - half, cy, cx + half, cy);
                break;

            case "V-LINE":
            case "VLINE":
                DrawLine(script, cx, cy - half, cx, cy + half);
                break;

            default:
                DrawCircle(script, cx, cy, half, settings.Direction);
                break;
        }
    }

    private static void DrawGrid(
        CAutomation1ScriptBase script,
        double cx,
        double cy,
        double half,
        int rows,
        int cols)
    {
        var rowCount = Math.Max(2, rows);
        var colCount = Math.Max(2, cols);
        var left = cx - half;
        var right = cx + half;
        var bottom = cy - half;
        var top = cy + half;

        for (var row = 0; row < rowCount; row++)
        {
            var y = bottom + ((top - bottom) * row / (rowCount - 1));
            if (row % 2 == 0)
            {
                DrawLine(script, left, y, right, y);
            }
            else
            {
                DrawLine(script, right, y, left, y);
            }
        }

        for (var col = 0; col < colCount; col++)
        {
            var x = left + ((right - left) * col / (colCount - 1));
            if (col % 2 == 0)
            {
                DrawLine(script, x, bottom, x, top);
            }
            else
            {
                DrawLine(script, x, top, x, bottom);
            }
        }
    }

    private static void DrawLine(
        CAutomation1ScriptBase script,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        script.Jump(startX, startY);
        script.SetMoveDelay(1.0);
        script.Mark(endX, endY);
        script.SetMoveDelay(1.0);
    }

    private static void DrawRect(
        CAutomation1ScriptBase script,
        double cx,
        double cy,
        double half,
        string direction)
    {
        var points = new List<(double X, double Y)>
        {
            (cx - half, cy - half),
            (cx + half, cy - half),
            (cx + half, cy + half),
            (cx - half, cy + half),
            (cx - half, cy - half)
        };

        if (direction.Equals("CCW", StringComparison.OrdinalIgnoreCase))
        {
            points.Reverse();
        }

        for (var index = 0; index < points.Count - 1; index++)
        {
            DrawLine(
                script,
                points[index].X,
                points[index].Y,
                points[index + 1].X,
                points[index + 1].Y);
        }
    }

    private static void DrawCircle(
        CAutomation1ScriptBase script,
        double cx,
        double cy,
        double radius,
        string direction)
    {
        if (radius <= 0.0)
        {
            radius = 0.0005;
        }

        var angle = direction.Equals("CCW", StringComparison.OrdinalIgnoreCase)
            ? -360.0
            : 360.0;
        var startX = cx - radius;
        var startY = cy;

        script.Jump(startX, startY);
        script.SetMoveDelay(1.0);
        script.Arc(startX, startY, startX, startY, cx, cy, angle);
        script.SetMoveDelay(1.0);
    }

    private static void EnsureAutomationResponse(
        string response,
        string action)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException($"{action} failed. Check Automation connection.");
        }
    }

    private bool TryReadManualParamFromScreen(
        out ST_MANUAL_SCAN_PARAM settings,
        out string errorMessage)
    {
        try
        {
            settings = ReadManualParamFromScreen();
            errorMessage = "";
            return true;
        }
        catch (InvalidDataException exception)
        {
            settings = CreateFallbackManualParam();
            errorMessage = exception.Message;
            return false;
        }
    }

    private ST_MANUAL_SCAN_PARAM ReadManualParamFromScreen()
    {
        return new ST_MANUAL_SCAN_PARAM(
            ReadRequiredDouble(ShapeSize, "Shape Size"),
            ReadRequiredDouble(ShapeOffsetX, "Offset X"),
            ReadRequiredDouble(ShapeOffsetY, "Offset Y"),
            string.IsNullOrWhiteSpace(ShapeDirection) ? "CW" : ShapeDirection.Trim(),
            ShapeName,
            ReadManualDouble("LaserPower"),
            ReadManualDouble("JumpSpeed"),
            ReadManualDouble("MarkSpeed"),
            0.0,
            ReadManualDouble("LaserFrequency"),
            ReadManualDouble("LaserOnDelay"),
            ReadManualDouble("LaserOffDelay"),
            10.0,
            48000,
            ReadGridLineCount(GridRowLines, "Grid Row Lines"),
            ReadGridLineCount(GridColLines, "Grid Col Lines"));
    }

    private void Apply(
        IReadOnlyList<ST_DISPLAY_ITEM> manualSettings,
        IReadOnlyList<ST_DISPLAY_ITEM> selectedHeadItems,
        IReadOnlyList<ST_DISPLAY_ITEM> positionMoveItems,
        IReadOnlyList<ST_DISPLAY_ITEM> shapeScanItems,
        IReadOnlyList<ST_DISPLAY_ITEM> commandStateItems,
        string selectedHead,
        string loadedSettingName,
        string loadedSettingPath,
        IReadOnlyList<ST_MANUAL_HEAD_CARD> headCards,
        IReadOnlyList<ST_MANUAL_SETTING_FILE> settingFiles,
        IReadOnlyList<ST_MANUAL_PARAMETER> settingParameters,
        IReadOnlyList<ST_MANUAL_COMMAND_STATE> commandStateRows)
    {
        ManualSettings = manualSettings;
        SelectedHeadItems = selectedHeadItems;
        PositionMoveItems = positionMoveItems;
        ShapeScanItems = shapeScanItems;
        CommandStateItems = commandStateItems;
        SelectedHead = selectedHead;
        LoadedSettingName = loadedSettingName;
        LoadedSettingPath = loadedSettingPath;
        HeadCards = headCards;
        SettingFiles = settingFiles;
        SettingParameters = settingParameters;
        CommandStateRows = commandStateRows;
    }

    private static IReadOnlyList<ST_DISPLAY_ITEM> BuildManualSettings(ST_MANUAL_SCAN_PARAM settings)
    {
        return
        [
            new("Laser Power", FormatDouble(settings.LaserPower, 2), "W"),
            new("Jump Speed", FormatDouble(settings.JumpSpeed, 3), "m/sec"),
            new("Mark Speed", FormatDouble(settings.MarkSpeed, 3), "m/sec"),
            new("Laser Frequency", FormatDouble(settings.LaserFrequency, 3), "kHz"),
            new("Laser On Delay", FormatDouble(settings.LaserOnDelay, 3), "usec"),
            new("Laser Off Delay", FormatDouble(settings.LaserOffDelay, 3), "usec")
        ];
    }

    private static IReadOnlyList<ST_MANUAL_HEAD_CARD> BuildHeadCards(int selectedHeadNo)
    {
        return
        [
            Head(1, "-12.345", "-23.450", "Ready", selectedHeadNo),
            Head(2, "15.230", "-10.125", "Ready", selectedHeadNo),
            Head(3, "-5.678", "-50.880", "Idle", selectedHeadNo),
            Head(4, "12.340", "8.960", "Ready", selectedHeadNo),
            Head(5, "-25.100", "30.250", "Idle", selectedHeadNo),
            Head(6, "-22.010", "11.250", "Ready", selectedHeadNo),
            Head(7, "18.750", "-18.430", "Ready", selectedHeadNo),
            Head(8, "-15.630", "15.400", "Idle", selectedHeadNo)
        ];
    }

    private static ST_MANUAL_HEAD_CARD Head(
        int headNo,
        string gx,
        string gy,
        string state,
        int selectedHeadNo)
    {
        return new ST_MANUAL_HEAD_CARD(
            headNo,
            $"H{headNo:00}",
            gx,
            gy,
            state,
            headNo == selectedHeadNo);
    }

    private static string ResolveSelectedSettingName(
        IReadOnlyList<string> settingNames,
        string selectedSettingName)
    {
        if (settingNames.Count == 0)
        {
            return "CIRCLE_TEST.scan";
        }

        var normalizedSelectedName = NormalizeSettingName(selectedSettingName);
        bool MatchName28(string name)
        {
            return name.Equals(normalizedSelectedName, StringComparison.OrdinalIgnoreCase);
        }

        bool MatchName29(string name)
        {
            return name.Equals("CIRCLE_TEST.scan", StringComparison.OrdinalIgnoreCase);
        }

        return settingNames.FirstOrDefault(MatchName28)
            ?? settingNames.FirstOrDefault(MatchName29)
            ?? settingNames[0];
    }

    private static IReadOnlyList<ST_MANUAL_SETTING_FILE> BuildSettingFiles(
        IReadOnlyList<string> settingNames,
        string selectedSettingName)
    {
        string GetNameSortKey30(string name)
        {
            return name;
        }

        ST_MANUAL_SETTING_FILE SelectName31(string name)
        {
            return new ST_MANUAL_SETTING_FILE(
                            name,
                            name.Equals(selectedSettingName, StringComparison.OrdinalIgnoreCase));
        }

        return settingNames
            .OrderBy(GetNameSortKey30, StringComparer.OrdinalIgnoreCase)
            .Select(SelectName31)
            .ToArray();
    }

    private static IReadOnlyList<ST_MANUAL_PARAMETER> BuildSettingParameters(
        ST_MANUAL_SCAN_PARAM settings,
        IReadOnlyList<ST_MANUAL_SCAN_FORM> formItems)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ShapeSize"] = FormatDouble(settings.ShapeSize, 3),
            ["OffsetX"] = FormatDouble(settings.OffsetX, 3),
            ["OffsetY"] = FormatDouble(settings.OffsetY, 3),
            ["Direction"] = settings.Direction,
            ["ShapeName"] = settings.ShapeName,
            ["GridRowLines"] = settings.GridRowLines.ToString(CultureInfo.InvariantCulture),
            ["GridColLines"] = settings.GridColLines.ToString(CultureInfo.InvariantCulture),
            ["LaserPower"] = FormatDouble(settings.LaserPower, 2),
            ["JumpSpeed"] = FormatDouble(settings.JumpSpeed, 3),
            ["MarkSpeed"] = FormatDouble(settings.MarkSpeed, 3),
            ["LaserFrequency"] = FormatDouble(settings.LaserFrequency, 3),
            ["LaserOnDelay"] = FormatDouble(settings.LaserOnDelay, 3),
            ["LaserOffDelay"] = FormatDouble(settings.LaserOffDelay, 3)
        };
        bool FilterItem32(ST_MANUAL_SCAN_FORM item)
        {
            return item.Show && item.Use;
        }

        int GetItemSortKey33(ST_MANUAL_SCAN_FORM item)
        {
            return item.DisplayOrder;
        }

        ST_MANUAL_PARAMETER SelectItem34(ST_MANUAL_SCAN_FORM item)
        {
            return new ST_MANUAL_PARAMETER(
                            item.Name,
                            item.DisplayName,
                            values.TryGetValue(item.Name, out var value) ? value : item.DefaultValue,
                            item.Unit,
                            item.DataType,
                            item.Min,
                            item.Max);
        }

        return formItems
            .Where(FilterItem32)
            .OrderBy(GetItemSortKey33)
            .Select(SelectItem34)
            .ToArray();
    }

    private IReadOnlyList<ST_MANUAL_COMMAND_STATE> BuildCommandStateRows(
        ST_MANUAL_HEAD_CARD selectedHead)
    {
        void HandleValueCallback35(string value)
        {
            _laserActionPower = value;
        }

        void HandleValueCallback36(string value)
        {
            _laserActionFrequency = value;
        }

        void HandleValueCallback37(string value)
        {
            LaserOnMode = value;
        }

        void HandleValueCallback38(string value)
        {
            LaserOnTimeMsec = value;
        }

        void HandleValueCallback39(string value)
        {
            LaserShotCount = value;
        }

        return
        [
            new("Selected Head", selectedHead.HeadName),
            new("Laser Power", _laserActionPower, "W", true, HandleValueCallback35),
            new("Frequency", _laserActionFrequency, "kHz", true, HandleValueCallback36),
            new("Laser On Mode", LaserOnMode, "-", true, HandleValueCallback37, NormalizeLaserOnMode),
            new("Laser On Time", LaserOnTimeMsec, "msec", true, HandleValueCallback38),
            new("Shot Count", LaserShotCount, "count", true, HandleValueCallback39)
        ];
    }

    private double ReadManualDouble(string parameterName)
    {
        var row = FindManualParameter(parameterName);
        var value = ReadManualText(row);

        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var result))
        {
            ValidateManualParameterRange(row, result);
            return result;
        }

        throw new InvalidDataException($"{parameterName} value is not a valid number: {value}");
    }

    private double ReadLaserMoveDelaySeconds(ST_MANUAL_SCAN_PARAM settings)
    {
        if (IsLaserTimeMode)
        {
            return ReadLaserOnTimeMsec() / 1000.0;
        }

        var shotCount = ReadLaserShotCount();
        if (settings.LaserFrequency <= 0.0)
        {
            throw new InvalidDataException("Laser Frequency must be greater than 0 for COUNT mode.");
        }

        return (shotCount + 1.0) / settings.LaserFrequency;
    }

    private ST_MANUAL_SCAN_PARAM ReadLaserActionSettings(ST_MANUAL_SCAN_PARAM manualSettings)
    {
        return manualSettings with
        {
            LaserPower = ReadRequiredDouble(_laserActionPower, "Laser Action Power"),
            LaserFrequency = ReadRequiredDouble(_laserActionFrequency, "Laser Action Frequency"),
            Time = ReadLaserOnTimeMsec(),
            Count = ReadLaserShotCount()
        };
    }

    private double ReadLaserOnTimeMsec()
    {
        return ReadRequiredDouble(LaserOnTimeMsec, "Laser On Time");
    }

    private int ReadLaserShotCount()
    {
        if (int.TryParse(
                LaserShotCount?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result))
        {
            return result;
        }

        throw new InvalidDataException($"Shot Count value is not a valid integer: {LaserShotCount}");
    }

    private static int ReadGridLineCount(
        string value,
        string name)
    {
        if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result))
        {
            throw new InvalidDataException($"{name} value is not a valid integer: {value}");
        }

        if (result < 2)
        {
            throw new InvalidDataException($"{name} must be greater than or equal to 2.");
        }

        return result;
    }

    private int ReadManualInt(string parameterName)
    {
        var row = FindManualParameter(parameterName);
        var value = ReadManualText(row);

        if (int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result))
        {
            ValidateManualParameterRange(row, result);
            return result;
        }

        throw new InvalidDataException($"{parameterName} value is not a valid integer: {value}");
    }

    private string ReadManualText(
        string parameterName,
        string defaultValue = "")
    {
        bool MatchParameter40(ST_MANUAL_PARAMETER parameter)
        {
            return parameter.Parameter.Equals(parameterName, StringComparison.OrdinalIgnoreCase);
        }

        var row = SettingParameters.FirstOrDefault(MatchParameter40);

        if (row is null)
        {
            if (!string.IsNullOrWhiteSpace(defaultValue))
            {
                return defaultValue.Trim();
            }

            throw new InvalidDataException($"{parameterName} parameter does not exist in Manual Setting.");
        }

        return ReadManualText(row, defaultValue);
    }

    private ST_MANUAL_PARAMETER FindManualParameter(string parameterName)
    {
        bool MatchParameter41(ST_MANUAL_PARAMETER parameter)
        {
            return parameter.Key.Equals(parameterName, StringComparison.OrdinalIgnoreCase) ||
                            parameter.Parameter.Equals(parameterName, StringComparison.OrdinalIgnoreCase);
        }

        return SettingParameters.FirstOrDefault(MatchParameter41)
            ?? throw new InvalidDataException($"{parameterName} parameter does not exist in Manual Setting.");
    }

    private static string ReadManualText(
        ST_MANUAL_PARAMETER row,
        string defaultValue = "")
    {
        var value = row.Value?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            return defaultValue.Trim();
        }

        throw new InvalidDataException($"{row.Parameter} value is empty.");
    }

    private static void ValidateManualParameterRange(
        ST_MANUAL_PARAMETER parameter,
        double value)
    {
        if (parameter.Min.Equals(parameter.Max))
        {
            return;
        }

        if (value < parameter.Min || value > parameter.Max)
        {
            throw new InvalidDataException(
                $"{parameter.Parameter} range warning. Input {value:0.###} {parameter.Unit} is outside {parameter.Min:0.###} - {parameter.Max:0.###} {parameter.Unit}.");
        }
    }

    private static double ReadRequiredDouble(
        string value,
        string name)
    {
        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var result))
        {
            return result;
        }

        throw new InvalidDataException($"{name} is not a valid number: {value}");
    }

    private static ST_MANUAL_SCAN_PARAM CreateFallbackManualParam()
    {
        return new ST_MANUAL_SCAN_PARAM(
            10.0,
            0.0,
            0.0,
            "CW",
            "Circle");
    }

    private static string NormalizeLaserOnMode(string? value)
    {
        return value?.Trim().Equals("COUNT", StringComparison.OrdinalIgnoreCase) == true
            ? "COUNT"
            : "TIME";
    }

    private static bool IsLaserTimeModeValue(string? value)
    {
        return !NormalizeLaserOnMode(value).Equals("COUNT", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetManualSettingNameFromParameter(object? parameter)
    {
        string EvaluateParameterSwitch1()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case ST_MANUAL_SETTING_FILE settingFile:
                    return settingFile.Name;
                case string text:
                    return text;
                default:
                    return "";
            }
        }

        var value = EvaluateParameterSwitch1();

        return NormalizeManualSettingNameInput(value);
    }

    private static string? ShowManualSettingNameDialog(
        string title,
        string message,
        string initialValue,
        Func<string, string>? validate = null)
    {
        var dialog = new CRecipeNameDialog(title, message, initialValue, validate)
        {
            Owner = GetActiveWindow()
        };

        return dialog.ShowDialog() == true
            ? NormalizeManualSettingNameInput(dialog.RecipeName)
            : null;
    }

    private static bool ConfirmManualSettingDelete(string settingName)
    {
        var dialog = new CRecipeConfirmDialog(
            "Delete Manual Setting",
            $"Delete {settingName}?\nThis operation removes the manual scan setting file from the Manual folder.",
            "DELETE")
        {
            Owner = GetActiveWindow()
        };

        return dialog.ShowDialog() == true;
    }

    private static void ShowManualWarning(string message)
    {
        MessageBox.Show(
            GetActiveWindow(),
            message,
            "Manual Scan Warning",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static Window? GetActiveWindow()
    {
        bool MatchWindow42(Window window)
        {
            return window.IsActive;
        }

        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(MatchWindow42);
    }

    private static string NormalizeManualSettingNameInput(string value)
    {
        var normalized = value.Trim();

        if (normalized.EndsWith(".scan", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }

        normalized = normalized.Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? ""
            : $"{normalized}.scan";
    }

    private static string ValidateManualSettingName(
        string settingName,
        IReadOnlyList<string> settingNames,
        string currentSettingName = "")
    {
        if (string.IsNullOrWhiteSpace(settingName))
        {
            return "Manual setting name is required.";
        }

        var settingId = Path.GetFileNameWithoutExtension(settingName.Trim());

        foreach (var character in settingId)
        {
            if (Path.GetInvalidFileNameChars().Contains(character))
            {
                return $"Manual setting name cannot contain '{character}'.";
            }
        }

        if (settingId is "." or ".." || settingId.EndsWith(".", StringComparison.Ordinal))
        {
            return "Manual setting name is not valid as a file name.";
        }

        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        if (reservedNames.Contains(settingId))
        {
            return "Manual setting name is reserved by Windows.";
        }
        bool CheckName43(string name)
        {
            return name.Equals(settingName, StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals(currentSettingName, StringComparison.OrdinalIgnoreCase);
        }

        var exists = settingNames.Any(CheckName43);

        return exists
            ? $"Manual setting {settingName} already exists."
            : "";
    }

    private static string NormalizeSettingName(string settingName)
    {
        var normalized = Path.GetFileName(settingName.Trim());

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "CIRCLE_TEST.scan";
        }

        return normalized.EndsWith(".scan", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}.scan";
    }

    private static string NormalizeScriptName(string commandName)
    {
        bool FilterCharacter44(char character)
        {
            return char.IsLetterOrDigit(character) || character == '_' || character == '-';
        }

        var normalized = new string(commandName
            .Where(FilterCharacter44)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? "MANUAL"
            : normalized.ToUpperInvariant();
    }

    private static string BuildShapeScanScriptName(
        int headNo,
        string shapeName)
    {
        var normalizedShape = NormalizeShapeName(shapeName)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant();

        return $"MANUAL_H{Math.Clamp(headNo, 1, 8):00}_SHAPE_{normalizedShape}";
    }

    private static string NormalizeShapeName(string shapeName)
    {
        var normalized = shapeName.Trim();
        string EvaluateValueSwitch2()
        {
            var switchValue = normalized.ToUpperInvariant();
            switch (switchValue)
            {
                case "DOT":
                    return "Dot";
                case "CROSS":
                    return "Cross";
                case "RECT" or "RECTANGLE":
                    return "Rect";
                case "GRID":
                    return "Grid";
                case "H-LINE" or "HLINE" or "H_LINE":
                    return "H-Line";
                case "V-LINE" or "VLINE" or "V_LINE":
                    return "V-Line";
                default:
                    return "Circle";
            }
        }

        return EvaluateValueSwitch2();
    }

    private static string FormatDouble(
        double value,
        int decimals)
    {
        var format = decimals <= 0
            ? "0"
            : $"0.{new string('0', decimals)}";

        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}

public sealed record ST_MANUAL_HEAD_CARD(
    int HeadNo,
    string HeadName,
    string Gx,
    string Gy,
    string State,
    bool IsSelected)
{
    public Brush StateBrush
    {
        get
        {
            return CStatusBrush.ForHeadStatus(State);
        }
    }
}

public sealed record ST_MANUAL_SETTING_FILE(
    string Name,
    bool IsSelected);

public sealed class ST_MANUAL_STAGE_AXIS : CBindingBase
{
    private string _targetPosition = "0.000";

    public ST_MANUAL_STAGE_AXIS(
        string displayAxis,
        string unit)
    {
        DisplayAxis = displayAxis;
        Unit = unit;
    }

    public string DisplayAxis { get; }

    public string Unit { get; }

    public string CurrentPosition
    {
        get
        {
            return "-";
        }
    }

    public string TargetPosition
    {
        get
        {
            return _targetPosition;
        }

        set
        {
            SetProperty(ref _targetPosition, value);
        }
    }
}

public sealed class ST_MANUAL_PARAMETER : CBindingBase
{
    private string _value;

    public ST_MANUAL_PARAMETER(
        string key,
        string parameter,
        string value,
        string unit,
        EN_RECIPE_DATA_TYPE dataType = EN_RECIPE_DATA_TYPE.String,
        double min = 0.0,
        double max = 0.0)
    {
        Key = key;
        Parameter = parameter;
        _value = value;
        Unit = unit;
        DataType = dataType;
        Min = min;
        Max = max;
    }

    public string Key { get; }

    public string Parameter { get; }

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

    public string Unit { get; }

    public EN_RECIPE_DATA_TYPE DataType { get; }

    public bool UsesSelectionEditor
    {
        get
        {
            return DataType == EN_RECIPE_DATA_TYPE.Bool;
        }
    }

    public IReadOnlyList<string> ValueOptions
    {
        get
        {
            return Value.Trim() is "0" or "1"
        ? ["0", "1"]
        : ["OFF", "ON"];
        }
    }

    public double Min { get; }

    public double Max { get; }
}

public sealed class ST_MANUAL_COMMAND_STATE : CBindingBase
{
    private readonly Action<string>? _valueChanged;
    private readonly Func<string, string>? _normalizeValue;
    private string _value;

    public ST_MANUAL_COMMAND_STATE(
        string name,
        string value,
        string unit = "",
        bool isEditable = false,
        Action<string>? valueChanged = null,
        Func<string, string>? normalizeValue = null)
    {
        Name = name;
        _value = value;
        Unit = unit;
        IsEditable = isEditable;
        _valueChanged = valueChanged;
        _normalizeValue = normalizeValue;
    }

    public string Name { get; }

    public string Value
    {
        get
        {
            return _value;
        }

        set
        {
            var nextValue = _normalizeValue?.Invoke(value) ?? value;
            if (SetProperty(ref _value, nextValue))
            {
                _valueChanged?.Invoke(nextValue);
            }
        }
    }

    public string Unit { get; }

    public bool IsEditable { get; }

    public bool IsReadOnly
    {
        get
        {
            return !IsEditable;
        }
    }

    public bool UsesSelectionEditor
    {
        get
        {
            return Name.Equals("Laser On Mode", StringComparison.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<string> ValueOptions { get; } = ["TIME", "COUNT"];
}
