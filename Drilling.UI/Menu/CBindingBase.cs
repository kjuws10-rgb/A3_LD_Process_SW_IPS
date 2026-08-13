using System.Runtime.CompilerServices;
using System.Windows;

namespace Drilling.UI.Menu;

public abstract class CBindingBase
{
    public event EventHandler? AllManagedItemsChanged;
    public event EventHandler? Arg1Changed;
    public event EventHandler? Arg2Changed;
    public event EventHandler? Arg3Changed;
    public event EventHandler? Arg4Changed;
    public event EventHandler? Arg5Changed;
    public event EventHandler? AutoConnectionChanged;
    public event EventHandler? AxisRowsChanged;
    public event EventHandler? BetTableRowsChanged;
    public event EventHandler? CanOperateSelectedInterfaceChanged;
    public event EventHandler? CellPreviewImageChanged;
    public event EventHandler? CellPreviewLabelsChanged;
    public event EventHandler? CommandHistoryRowsChanged;
    public event EventHandler? CommandStateItemsChanged;
    public event EventHandler? CommandStateRowsChanged;
    public event EventHandler? CoordinateBasisDescriptionChanged;
    public event EventHandler? CoordinateBasisOptionsChanged;
    public event EventHandler? CoordinateCellPreviewLabelsChanged;
    public event EventHandler? CoordinateGlassPreviewImageChanged;
    public event EventHandler? CoordinateGlassPreviewSummaryChanged;
    public event EventHandler? CoordinateHoleMatrixRowsChanged;
    public event EventHandler? CoordinateIsCellDetailVisibleChanged;
    public event EventHandler? CoordinateIsGlassPreviewVisibleChanged;
    public event EventHandler? CoordinateSelectedBasisNameChanged;
    public event EventHandler? CoordinateSelectedCellNameChanged;
    public event EventHandler? CoordinateSelectedHoleNameChanged;
    public event EventHandler? CoordinateSelectedRecipeNameChanged;
    public event EventHandler? CoordinateValueRowsChanged;
    public event EventHandler? CountXChanged;
    public event EventHandler? CountYChanged;
    public event EventHandler? CurrentCellIndicatorTextChanged;
    public event EventHandler? CurrentDateTextChanged;
    public event EventHandler? CurrentScreenChanged;
    public event EventHandler? CurrentStepDetailsChanged;
    public event EventHandler? CurrentTimeTextChanged;
    public event EventHandler? CurrentUserTextChanged;
    public event EventHandler? CycleItemsChanged;
    public event EventHandler? DeviceChanged;
    public event EventHandler? DeviceTabsChanged;
    public event EventHandler? DivChanged;
    public event EventHandler? ElapsedTimeTextChanged;
    public event EventHandler? EstimatedTimeTextChanged;
    public event EventHandler? FirstXChanged;
    public event EventHandler? FirstYChanged;
    public event EventHandler? GridColLinesChanged;
    public event EventHandler? GridRowLinesChanged;
    public event EventHandler? HasVisionImageChanged;
    public event EventHandler? HeadDeviceSelectorTitleChanged;
    public event EventHandler? HeadPreviewImageChanged;
    public event EventHandler? HeadPreviewLabelsChanged;
    public event EventHandler? HeadPreviewSummaryTextChanged;
    public event EventHandler? HeadSelectRowsChanged;
    public event EventHandler? HistoryPanelTitleChanged;
    public event EventHandler? HoleMatrixColumnHeadersChanged;
    public event EventHandler? HoleMatrixRowsChanged;
    public event EventHandler? HoleRowsChanged;
    public event EventHandler? InputRowsChanged;
    public event EventHandler? InspectionModeTextChanged;
    public event EventHandler? InspectionRuleTextChanged;
    public event EventHandler? InspectionRuleVisibilityChanged;
    public event EventHandler? InspectionStatusItemsChanged;
    public event EventHandler? InspectionSummaryChanged;
    public event EventHandler? InterlockItemsChanged;
    public event EventHandler? IsAttenuatorChanged;
    public event EventHandler? IsBetChanged;
    public event EventHandler? IsCellDetailVisibleChanged;
    public event EventHandler? IsCellPreviewVisibleChanged;
    public event EventHandler? IsChillerChanged;
    public event EventHandler? IsCoordinateViewerChanged;
    public event EventHandler? IsEditedChanged;
    public event EventHandler? IsGenericDeviceChanged;
    public event EventHandler? IsIoChanged;
    public event EventHandler? IsLaserChanged;
    public event EventHandler? IsLaserCountModeChanged;
    public event EventHandler? IsLaserTimeModeChanged;
    public event EventHandler? IsMelsecChanged;
    public event EventHandler? IsModifiedChanged;
    public event EventHandler? IsMotorChanged;
    public event EventHandler? IsPowerMeterChanged;
    public event EventHandler? IsProductChanged;
    public event EventHandler? IsScannerWorkspaceChanged;
    public event EventHandler? IsSelectedChanged;
    public event EventHandler? IsSimulationChanged;
    public event EventHandler? IsStageVisionWorkspaceChanged;
    public event EventHandler? LaserControlRowsChanged;
    public event EventHandler? LaserOnModeChanged;
    public event EventHandler? LaserOnSegmentsTextChanged;
    public event EventHandler? LaserOnTimeMsecChanged;
    public event EventHandler? LaserShotCountChanged;
    public event EventHandler? LifecycleItemsChanged;
    public event EventHandler? MagChanged;
    public event EventHandler? MelsecGroupsChanged;
    public event EventHandler? MelsecReadRowsChanged;
    public event EventHandler? MelsecRowsChanged;
    public event EventHandler? MelsecWriteRowsChanged;
    public event EventHandler? ModifiedBrushChanged;
    public event EventHandler? ModifiedTextChanged;
    public event EventHandler? MoveCountTextChanged;
    public event EventHandler? NickNameChanged;
    public event EventHandler? NumberChanged;
    public event EventHandler? OffsetCoordinateTextChanged;
    public event EventHandler? OffsetXChanged;
    public event EventHandler? OffsetYChanged;
    public event EventHandler? OperationButtonsChanged;
    public event EventHandler? OperationFieldsChanged;
    public event EventHandler? OperationPanelTitleChanged;
    public event EventHandler? OutputRowsChanged;
    public event EventHandler? ParameterPanelTitleChanged;
    public event EventHandler? ParameterRowsChanged;
    public event EventHandler? PicoAllMotorSelectButtonsChanged;
    public event EventHandler? PicoConnectionButtonsChanged;
    public event EventHandler? PicoMotorSelectButtonsChanged;
    public event EventHandler? PitchXChanged;
    public event EventHandler? PitchYChanged;
    public event EventHandler? PixelSizeChanged;
    public event EventHandler? PositionRowsChanged;
    public event EventHandler? ProcessResultBrushChanged;
    public event EventHandler? ProcessResultValueChanged;
    public event EventHandler? ProcessSequenceItemsChanged;
    public event EventHandler? ProcessStepChanged;
    public event EventHandler? ProcessSummaryItemsChanged;
    public event EventHandler? ProductHeadRowsChanged;
    public event EventHandler? ProductHistoryRowsChanged;
    public event EventHandler? ProductItemsChanged;
    public event EventHandler? ProgressPercentChanged;
    public event EventHandler? ProgressPercentTextChanged;
    public event EventHandler? ProgressTextChanged;
    public event EventHandler? PwmDeviceRowsChanged;
    public event EventHandler? PwmProcessButtonsChanged;
    public event EventHandler? PwmProcessRowsChanged;
    public event EventHandler? PwmRunButtonsChanged;
    public event EventHandler? PwmSettingRowsChanged;
    public event EventHandler? PwmStepButtonsChanged;
    public event EventHandler? PwmStepRowsChanged;
    public event EventHandler? ResultItemsChanged;
    public event EventHandler? ResultMessageChanged;
    public event EventHandler? RotationChanged;
    public event EventHandler? ScannerStatusItemsChanged;
    public event EventHandler? ScriptStatusChanged;
    public event EventHandler? ScriptStatusTextChanged;
    public event EventHandler? ScriptTaskStatusItemsChanged;
    public event EventHandler? SelectedAxisIdChanged;
    public event EventHandler? SelectedAxisRowChanged;
    public event EventHandler? SelectedCellHoleTitleChanged;
    public event EventHandler? SelectedHeadDeviceNameChanged;
    public event EventHandler? SelectedHoleChanged;
    public event EventHandler? SelectedHoleIndicatorTextChanged;
    public event EventHandler? SelectedInterfaceRowChanged;
    public event EventHandler? SelectedLaserNameChanged;
    public event EventHandler? SelectedMenuChanged;
    public event EventHandler? SelectedPwmProcessRowChanged;
    public event EventHandler? SelectedPwmStepRowChanged;
    public event EventHandler? SelectedRuleFileChanged;
    public event EventHandler? SelectedSampleCellChanged;
    public event EventHandler? SelectedSampleHeadChanged;
    public event EventHandler? SelectedTabChanged;
    public event EventHandler? ShapeDirectionChanged;
    public event EventHandler? ShapeNameChanged;
    public event EventHandler? ShapeOffsetXChanged;
    public event EventHandler? ShapeOffsetYChanged;
    public event EventHandler? ShapeSizeChanged;
    public event EventHandler? SimulChanged;
    public event EventHandler? SimulBrushChanged;
    public event EventHandler? SpotSizeChanged;
    public event EventHandler? StatusMessageChanged;
    public event EventHandler? StatusPanelTitleChanged;
    public event EventHandler? StatusRowsChanged;
    public event EventHandler? SubtitleChanged;
    public event EventHandler? SummaryItemsChanged;
    public event EventHandler? SystemSectionChanged;
    public event EventHandler? TabsChanged;
    public event EventHandler? TargetGxChanged;
    public event EventHandler? TargetGyChanged;
    public event EventHandler? TargetPositionChanged;
    public event EventHandler? ThemeModeTextChanged;
    public event EventHandler? ThemeToggleTextChanged;
    public event EventHandler? TitleChanged;
    public event EventHandler? TotalPointsTextChanged;
    public event EventHandler? TrendPanelTitleChanged;
    public event EventHandler? TrendPointsChanged;
    public event EventHandler? TypeChanged;
    public event EventHandler? ValueChanged;
    public event EventHandler? ValueBrushChanged;
    public event EventHandler? ValueStateChanged;
    public event EventHandler? VisionCaptureStatusChanged;
    public event EventHandler? VisionCaptureTimeChanged;
    public event EventHandler? VisionImageChanged;

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            Action<string?> dispatchPropertyChanged = OnPropertyChanged;
            dispatcher.Invoke(dispatchPropertyChanged, propertyName);
            return;
        }

        switch (propertyName)
        {
            case "AllManagedItems":
                AllManagedItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Arg1":
                Arg1Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "Arg2":
                Arg2Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "Arg3":
                Arg3Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "Arg4":
                Arg4Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "Arg5":
                Arg5Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "AutoConnection":
                AutoConnectionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "AxisRows":
                AxisRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "BetTableRows":
                BetTableRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CanOperateSelectedInterface":
                CanOperateSelectedInterfaceChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CellPreviewImage":
                CellPreviewImageChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CellPreviewLabels":
                CellPreviewLabelsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CommandHistoryRows":
                CommandHistoryRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CommandStateItems":
                CommandStateItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CommandStateRows":
                CommandStateRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateBasisDescription":
                CoordinateBasisDescriptionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateBasisOptions":
                CoordinateBasisOptionsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateCellPreviewLabels":
                CoordinateCellPreviewLabelsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateGlassPreviewImage":
                CoordinateGlassPreviewImageChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateGlassPreviewSummary":
                CoordinateGlassPreviewSummaryChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateHoleMatrixRows":
                CoordinateHoleMatrixRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateIsCellDetailVisible":
                CoordinateIsCellDetailVisibleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateIsGlassPreviewVisible":
                CoordinateIsGlassPreviewVisibleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateSelectedBasisName":
                CoordinateSelectedBasisNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateSelectedCellName":
                CoordinateSelectedCellNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateSelectedHoleName":
                CoordinateSelectedHoleNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateSelectedRecipeName":
                CoordinateSelectedRecipeNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CoordinateValueRows":
                CoordinateValueRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CountX":
                CountXChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CountY":
                CountYChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CurrentCellIndicatorText":
                CurrentCellIndicatorTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CurrentDateText":
                CurrentDateTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CurrentScreen":
                CurrentScreenChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CurrentStepDetails":
                CurrentStepDetailsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CurrentTimeText":
                CurrentTimeTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CurrentUserText":
                CurrentUserTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "CycleItems":
                CycleItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Device":
                DeviceChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "DeviceTabs":
                DeviceTabsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Div":
                DivChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ElapsedTimeText":
                ElapsedTimeTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "EstimatedTimeText":
                EstimatedTimeTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "FirstX":
                FirstXChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "FirstY":
                FirstYChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "GridColLines":
                GridColLinesChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "GridRowLines":
                GridRowLinesChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HasVisionImage":
                HasVisionImageChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HeadDeviceSelectorTitle":
                HeadDeviceSelectorTitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HeadPreviewImage":
                HeadPreviewImageChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HeadPreviewLabels":
                HeadPreviewLabelsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HeadPreviewSummaryText":
                HeadPreviewSummaryTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HeadSelectRows":
                HeadSelectRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HistoryPanelTitle":
                HistoryPanelTitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HoleMatrixColumnHeaders":
                HoleMatrixColumnHeadersChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HoleMatrixRows":
                HoleMatrixRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "HoleRows":
                HoleRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "InputRows":
                InputRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "InspectionModeText":
                InspectionModeTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "InspectionRuleText":
                InspectionRuleTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "InspectionRuleVisibility":
                InspectionRuleVisibilityChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "InspectionStatusItems":
                InspectionStatusItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "InspectionSummary":
                InspectionSummaryChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "InterlockItems":
                InterlockItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsAttenuator":
                IsAttenuatorChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsBet":
                IsBetChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsCellDetailVisible":
                IsCellDetailVisibleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsCellPreviewVisible":
                IsCellPreviewVisibleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsChiller":
                IsChillerChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsCoordinateViewer":
                IsCoordinateViewerChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsEdited":
                IsEditedChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsGenericDevice":
                IsGenericDeviceChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsIo":
                IsIoChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsLaser":
                IsLaserChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsLaserCountMode":
                IsLaserCountModeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsLaserTimeMode":
                IsLaserTimeModeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsMelsec":
                IsMelsecChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsModified":
                IsModifiedChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsMotor":
                IsMotorChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsPowerMeter":
                IsPowerMeterChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsProduct":
                IsProductChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsScannerWorkspace":
                IsScannerWorkspaceChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsSelected":
                IsSelectedChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsSimulation":
                IsSimulationChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "IsStageVisionWorkspace":
                IsStageVisionWorkspaceChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "LaserControlRows":
                LaserControlRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "LaserOnMode":
                LaserOnModeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "LaserOnSegmentsText":
                LaserOnSegmentsTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "LaserOnTimeMsec":
                LaserOnTimeMsecChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "LaserShotCount":
                LaserShotCountChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "LifecycleItems":
                LifecycleItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Mag":
                MagChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "MelsecGroups":
                MelsecGroupsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "MelsecReadRows":
                MelsecReadRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "MelsecRows":
                MelsecRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "MelsecWriteRows":
                MelsecWriteRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ModifiedBrush":
                ModifiedBrushChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ModifiedText":
                ModifiedTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "MoveCountText":
                MoveCountTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "NickName":
                NickNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Number":
                NumberChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "OffsetCoordinateText":
                OffsetCoordinateTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "OffsetX":
                OffsetXChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "OffsetY":
                OffsetYChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "OperationButtons":
                OperationButtonsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "OperationFields":
                OperationFieldsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "OperationPanelTitle":
                OperationPanelTitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "OutputRows":
                OutputRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ParameterPanelTitle":
                ParameterPanelTitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ParameterRows":
                ParameterRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PicoAllMotorSelectButtons":
                PicoAllMotorSelectButtonsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PicoConnectionButtons":
                PicoConnectionButtonsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PicoMotorSelectButtons":
                PicoMotorSelectButtonsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PitchX":
                PitchXChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PitchY":
                PitchYChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PixelSize":
                PixelSizeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PositionRows":
                PositionRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProcessResultBrush":
                ProcessResultBrushChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProcessResultValue":
                ProcessResultValueChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProcessSequenceItems":
                ProcessSequenceItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProcessStep":
                ProcessStepChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProcessSummaryItems":
                ProcessSummaryItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProductHeadRows":
                ProductHeadRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProductHistoryRows":
                ProductHistoryRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProductItems":
                ProductItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProgressPercent":
                ProgressPercentChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProgressPercentText":
                ProgressPercentTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ProgressText":
                ProgressTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PwmDeviceRows":
                PwmDeviceRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PwmProcessButtons":
                PwmProcessButtonsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PwmProcessRows":
                PwmProcessRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PwmRunButtons":
                PwmRunButtonsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PwmSettingRows":
                PwmSettingRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PwmStepButtons":
                PwmStepButtonsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "PwmStepRows":
                PwmStepRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ResultItems":
                ResultItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ResultMessage":
                ResultMessageChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Rotation":
                RotationChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ScannerStatusItems":
                ScannerStatusItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ScriptStatus":
                ScriptStatusChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ScriptStatusText":
                ScriptStatusTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ScriptTaskStatusItems":
                ScriptTaskStatusItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedAxisId":
                SelectedAxisIdChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedAxisRow":
                SelectedAxisRowChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedCellHoleTitle":
                SelectedCellHoleTitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedHeadDeviceName":
                SelectedHeadDeviceNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedHole":
                SelectedHoleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedHoleIndicatorText":
                SelectedHoleIndicatorTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedInterfaceRow":
                SelectedInterfaceRowChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedLaserName":
                SelectedLaserNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedMenu":
                SelectedMenuChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedPwmProcessRow":
                SelectedPwmProcessRowChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedPwmStepRow":
                SelectedPwmStepRowChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedRuleFile":
                SelectedRuleFileChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedSampleCell":
                SelectedSampleCellChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedSampleHead":
                SelectedSampleHeadChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SelectedTab":
                SelectedTabChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ShapeDirection":
                ShapeDirectionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ShapeName":
                ShapeNameChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ShapeOffsetX":
                ShapeOffsetXChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ShapeOffsetY":
                ShapeOffsetYChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ShapeSize":
                ShapeSizeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Simul":
                SimulChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SimulBrush":
                SimulBrushChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SpotSize":
                SpotSizeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "StatusMessage":
                StatusMessageChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "StatusPanelTitle":
                StatusPanelTitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "StatusRows":
                StatusRowsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Subtitle":
                SubtitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SummaryItems":
                SummaryItemsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "SystemSection":
                SystemSectionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Tabs":
                TabsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "TargetGx":
                TargetGxChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "TargetGy":
                TargetGyChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "TargetPosition":
                TargetPositionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ThemeModeText":
                ThemeModeTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ThemeToggleText":
                ThemeToggleTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Title":
                TitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "TotalPointsText":
                TotalPointsTextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "TrendPanelTitle":
                TrendPanelTitleChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "TrendPoints":
                TrendPointsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Type":
                TypeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "Value":
                ValueChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ValueBrush":
                ValueBrushChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "ValueState":
                ValueStateChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "VisionCaptureStatus":
                VisionCaptureStatusChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "VisionCaptureTime":
                VisionCaptureTimeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "VisionImage":
                VisionImageChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
