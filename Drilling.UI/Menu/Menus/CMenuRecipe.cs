using System.Globalization;
using System.IO;
using System.Windows;
using Drilling.UI.Popup;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using Drilling.Common.Recipe;
using Drilling.Common.Review;
using System.Windows.Media;

namespace Drilling.UI.Menu.Menus;

public sealed class CMenuRecipe : CMenuBase
{
    private static readonly string[] CellParameterKeys =
    [
        "CELL_ALIGN_TO_1ST_PIXEL_X",
        "CELL_ALIGN_TO_1ST_PIXEL_Y",
        "CELL_ROTATION",
        "PIXEL_SIZE",
        "NUM_OF_PIXEL_X",
        "NUM_OF_PIXEL_Y",
        "PITCH_X",
        "PITCH_Y",
        "CHESS",
        "SPLITED_BEAM_COUNT"
    ];

    private static readonly string[] GlassSettingKeys =
    [
        "GLASS_SIZE_X",
        "GLASS_SIZE_Y",
        "CELL_COUNT",
        "AK_MARGIN_X",
        "AK_MARGIN_Y",
        "DISTORTION_KEY1_X",
        "DISTORTION_KEY1_Y",
        "DISTORTION_KEY2_X",
        "DISTORTION_KEY2_Y",
        "DISTORTION_KEY3_X",
        "DISTORTION_KEY3_Y",
        "DISTORTION_KEY4_X",
        "DISTORTION_KEY4_Y",
        "DISTORTION_KEY5_X",
        "DISTORTION_KEY5_Y",
        "DISTORTION_KEY6_X",
        "DISTORTION_KEY6_Y"
    ];

    private readonly CRecipeManager _recipeManager;
    private readonly CSettingManager _settingManager;
    private readonly Func<string> _selectedRecipeIdProvider;
    private readonly Action<string> _selectedRecipeIdSetter;
    private readonly Func<string> _selectedCategoryProvider;
    private readonly Action<string> _selectedCategorySetter;
    private string _selectedGroup = "ALL";
    private int _selectedCellNo = 1;
    private readonly HashSet<int> _selectedOverviewCells = [];
    private int _selectedHoleNo = 1;
    private ST_RECIPE_HOLE_ROW? _selectedHole;
    private IReadOnlyList<ST_RECIPE_MANAGED_ITEM> _previewTrackedItems = [];
    private IReadOnlyList<ST_SYSTEM_PARAMETER> _headPreviewSettings = [];
    private CancellationTokenSource? _previewRefreshCancellation;
    private readonly Func<CMenuRecipe?> _editScreenProvider;
    private readonly Action<string> _setStatusMessage;
    private readonly Action<EN_MENU, string> _showLoadingScreen;
    private readonly Action _refreshShellStatus;
    private readonly Func<Task> _refreshCurrentScreen;

    public CMenuRecipe(
        CRecipeManager recipeManager,
        CSettingManager settingManager,
        Func<string> selectedRecipeIdProvider,
        Action<string> selectedRecipeIdSetter,
        Func<string> selectedCategoryProvider,
        Action<string> selectedCategorySetter,
        Func<CMenuRecipe?> editScreenProvider,
        Action<string> setStatusMessage,
        Action<EN_MENU, string> showLoadingScreen,
        Action refreshShellStatus,
        Func<Task> refreshCurrentScreen)
    {
        _recipeManager = recipeManager;
        _settingManager = settingManager;
        _selectedRecipeIdProvider = selectedRecipeIdProvider;
        _selectedRecipeIdSetter = selectedRecipeIdSetter;
        _selectedCategoryProvider = selectedCategoryProvider;
        _selectedCategorySetter = selectedCategorySetter;
        _editScreenProvider = editScreenProvider;
        _setStatusMessage = setStatusMessage;
        _showLoadingScreen = showLoadingScreen;
        _refreshShellStatus = refreshShellStatus;
        _refreshCurrentScreen = refreshCurrentScreen;

        async void HandleSelectCommand1(object? parameter)
        {
            await Select(parameter);
        }

        SelectCommand = new CButtonCommand(HandleSelectCommand1);

        async void HandleSelectCategoryCommand2(object? parameter)
        {
            await SelectCategory(parameter);
        }

        SelectCategoryCommand = new CButtonCommand(HandleSelectCategoryCommand2);

        async void HandleSelectGroupCommand3(object? parameter)
        {
            await SelectGroup(parameter);
        }

        SelectGroupCommand = new CButtonCommand(HandleSelectGroupCommand3);

        async void HandleSelectCellCommand4(object? parameter)
        {
            await SelectCell(parameter);
        }

        SelectCellCommand = new CButtonCommand(HandleSelectCellCommand4);

        async void HandleSelectPreviewCellCommand5(object? parameter)
        {
            await SelectPreviewCell(parameter);
        }

        SelectPreviewCellCommand = new CButtonCommand(HandleSelectPreviewCellCommand5);
        void HandleBackToCellPreviewCommand6(object? _)
        {
            SetCellDetailVisible(false);
        }

        BackToCellPreviewCommand = new CButtonCommand(HandleBackToCellPreviewCommand6);
        SelectHoleCommand = new CButtonCommand(SelectHole);
        void HandleSelectAllCellsCommand7(object? _)
        {
            SetAllOverviewCellsSelected(true);
        }

        SelectAllCellsCommand = new CButtonCommand(HandleSelectAllCellsCommand7);
        void HandleClearCellSelectionCommand8(object? _)
        {
            SetAllOverviewCellsSelected(false);
        }

        ClearCellSelectionCommand = new CButtonCommand(HandleClearCellSelectionCommand8);

        async void HandleApplyPointPatternCommand9(object? _)
        {
            await ApplyPointPatternToSelectedCells();
        }

        bool HandleApplyPointPatternCommand10(object? _)
        {
            return CanApplyPointPattern;
        }

        ApplyPointPatternCommand = new CButtonCommand(
HandleApplyPointPatternCommand9,
HandleApplyPointPatternCommand10);

        async void HandleCreateCommand11(object? _)
        {
            await Create();
        }

        CreateCommand = new CButtonCommand(HandleCreateCommand11);

        async void HandleModifyCommand12(object? _)
        {
            await Modify();
        }

        ModifyCommand = new CButtonCommand(HandleModifyCommand12);

        async void HandleSaveCommand13(object? _)
        {
            await Save();
        }

        SaveCommand = new CButtonCommand(HandleSaveCommand13);

        async void HandleDeleteCommand14(object? _)
        {
            await Delete();
        }

        DeleteCommand = new CButtonCommand(HandleDeleteCommand14);
    }

    public override EN_MENU Menu
    {
        get
        {
            return EN_MENU.Recipe;
        }
    }

    public IReadOnlyList<ST_DISPLAY_ITEM> RecipeList { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> Parameters { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> History { get; private set; } = [];

    public IReadOnlyList<ST_DISPLAY_ITEM> Actions { get; private set; } = [];

    public string SelectedRecipeFile { get; private set; } = "";

    public IReadOnlyList<ST_RECIPE_FILE> RecipeFiles { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_CATEGORY_TAB> ItemTabs { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_GROUP_TAB> GroupTabs { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_CELL_OVERVIEW_ROW> CellOverviewRows { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_HOLE_ROW> HoleRows { get; private set; } = [];

    public IReadOnlyList<string> HoleMatrixColumnHeaders { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_HOLE_MATRIX_ROW> HoleMatrixRows { get; private set; } = [];

    public ST_RECIPE_HOLE_ROW? SelectedHole
    {
        get
        {
            return _selectedHole;
        }

        set
        {
            if (!SetProperty(ref _selectedHole, value))
            {
                return;
            }

            if (value is not null)
            {
                _selectedHoleNo = value.HoleNo;
            }

            foreach (var row in HoleRows)
            {
                row.IsSelected = value is not null && row.HoleNo == value.HoleNo;
            }

            OnPropertyChanged(nameof(SelectedHoleIndicatorText));
        }
    }

    public ST_RECIPE_MANAGED_ITEM? GlassSizeXItem { get; private set; }

    public ST_RECIPE_MANAGED_ITEM? GlassSizeYItem { get; private set; }

    public ST_RECIPE_MANAGED_ITEM? CellCountItem { get; private set; }

    public ST_RECIPE_MANAGED_ITEM? AkMarginXItem { get; private set; }

    public ST_RECIPE_MANAGED_ITEM? AkMarginYItem { get; private set; }

    public IReadOnlyList<ST_RECIPE_DISTORTION_KEY_ITEM> DistortionKeyItems { get; private set; } = [];

    public bool IsCellCategory { get; private set; }

    public bool IsHeadCategory { get; private set; }

    private bool _isCellDetailVisible;
    public bool IsCellDetailVisible
    {
        get
        {
            return _isCellDetailVisible;
        }
    }

    public bool IsCellPreviewVisible
    {
        get
        {
            return !_isCellDetailVisible;
        }
    }

    public ImageSource? CellPreviewImage { get; private set; }

    public IReadOnlyList<ST_CELL_PREVIEW_LABEL> CellPreviewLabels { get; private set; } = [];

    public ImageSource? HeadPreviewImage { get; private set; }

    public IReadOnlyList<ST_HEAD_COVERAGE_PREVIEW_LABEL> HeadPreviewLabels { get; private set; } = [];

    public string HeadPreviewSummaryText { get; private set; } = "";

    public string CurrentCellIndicatorText
    {
        get
        {
            return $"CURRENT: Cell{_selectedCellNo}";
        }
    }

    public string SelectedCellHoleTitle
    {
        get
        {
            return $"SELECTED CELL HOLES - Cell{_selectedCellNo} / {HoleRows.Count:N0} HOLES";
        }
    }

    public string SelectedHoleIndicatorText
    {
        get
        {
            return SelectedHole is null ? "NO HOLE SELECTED" : SelectedHole.MatrixPointName;
        }
    }

    private int PointPatternTargetCount
    {
        get
        {
            bool CountRowCallback15(ST_RECIPE_CELL_OVERVIEW_ROW row)
            {
                return row.CellNo != _selectedCellNo && row.IsSelected;
            }

            return CellOverviewRows.Count(CountRowCallback15);
        }
    }

    private bool CanApplyPointPattern
    {
        get
        {
            return PointPatternTargetCount > 0;
        }
    }

    public string SelectedGroup { get; private set; } = "ALL";

    public IReadOnlyList<ST_RECIPE_MANAGED_ITEM> AllManagedItems { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_MANAGED_ITEM> ManagedItems { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_HISTORY_ROW> ChangeHistory { get; private set; } = [];

    public IReadOnlyList<ST_RECIPE_STATE_ROW> StateRows { get; private set; } = [];

    public CButtonCommand SelectCommand { get; }

    public CButtonCommand SelectCategoryCommand { get; }

    public CButtonCommand SelectGroupCommand { get; }

    public CButtonCommand SelectCellCommand { get; }

    public CButtonCommand SelectPreviewCellCommand { get; }

    public CButtonCommand BackToCellPreviewCommand { get; }

    public CButtonCommand SelectHoleCommand { get; }

    public CButtonCommand SelectAllCellsCommand { get; }

    public CButtonCommand ClearCellSelectionCommand { get; }

    public CButtonCommand ApplyPointPatternCommand { get; }

    public CButtonCommand CreateCommand { get; }

    public CButtonCommand ModifyCommand { get; }

    public CButtonCommand SaveCommand { get; }

    public CButtonCommand DeleteCommand { get; }

    public async override Task<CScreenViewModel> Build(CancellationToken cancellationToken = default)
    {
        var recipes = await _recipeManager.LoadRecipes(cancellationToken);
        var optionSettings = await _settingManager.LoadSection(EN_SETTING_TAB.Option, cancellationToken);
        var recipe = GetSelectedRecipe(recipes, _selectedRecipeIdProvider());
        var selectedRecipeFile = GetRecipeFileName(recipe);
        var loadedManagedItems = BuildManagedItems(recipe);
        var allManagedItems = GetEditItems(loadedManagedItems, _editScreenProvider(), selectedRecipeFile);
        allManagedItems = EnsureCellPointItems(allManagedItems, GetCellCount(allManagedItems));
        var categories = BuildCategories(allManagedItems);
        var selectedCategory = NormalizeCategory(_selectedCategoryProvider(), categories);
        var isCellCategory = selectedCategory.Equals("CELL", StringComparison.OrdinalIgnoreCase);
        var isHeadCategory = selectedCategory.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
        var cellCount = GetCellCount(allManagedItems);
        var cells = BuildCells(allManagedItems, cellCount);
        _selectedCellNo = Math.Clamp(_selectedCellNo, 1, cellCount);
        bool RemoveWhereCellNoCallback16(int cellNo)
        {
            return cellNo < 1 || cellNo > cellCount;
        }

        _selectedOverviewCells.RemoveWhere(RemoveWhereCellNoCallback16);
        bool MatchCell17(ST_RECIPE_CELL cell)
        {
            return cell.CellNo == _selectedCellNo;
        }

        bool FilterItem18(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase) &&
                                    !IsMovedToCellTab(item);
        }

        var categoryFilteredItems = isCellCategory
            ? cells.First(MatchCell17).Items
            : string.IsNullOrWhiteSpace(selectedCategory)
                ? []
                : allManagedItems
                    .Where(FilterItem18)
                    .ToArray();
        var groups = BuildGroups(categoryFilteredItems);
        var selectedGroup = isCellCategory ? "ALL" : NormalizeGroup(_selectedGroup, groups);
        bool FilterItem19(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.SourceGroup.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase);
        }

        var filteredManagedItems = isCellCategory || selectedGroup == "ALL"
            ? categoryFilteredItems
            : categoryFilteredItems.Where(FilterItem19).ToArray();
        ST_DISPLAY_ITEM SelectItem20(ST_RECIPE_DATA item)
        {
            return new ST_DISPLAY_ITEM(item.Id, item.Name);
        }

        ST_DISPLAY_ITEM SelectItem21(ST_RECIPE_PARAM item)
        {
            return new ST_DISPLAY_ITEM(item.Name, item.Value, $"{item.Unit} / {item.Range}");
        }

        ST_DISPLAY_ITEM SelectItem22(ST_RECIPE_HISTORY item)
        {
            return new ST_DISPLAY_ITEM(item.ChangedAt.ToString("yyyy-MM-dd HH:mm"), item.ItemName, $"{item.OldValue} -> {item.NewValue} / {item.OperatorId}");
        }

        Apply(
            recipes.Select(SelectItem20).ToArray(),
            recipe?.Parameters.Select(SelectItem21).ToArray() ?? [],
            recipe?.History.Select(SelectItem22).ToArray() ?? [],
            BuildActions(),
            selectedRecipeFile,
            BuildRecipeFiles(recipes, recipe),
            BuildCategoryTabs(categories, selectedCategory),
            BuildGroupTabs(groups, selectedGroup),
            BuildCellOverviewRows(cells),
            isCellCategory,
            isCellCategory
                ? BuildLayoutPreview(allManagedItems, cells, _selectedCellNo)
                : null,
            isHeadCategory,
            isHeadCategory
                ? BuildHeadPreview(allManagedItems, optionSettings, selectedGroup)
                : null,
            optionSettings,
            selectedGroup,
            allManagedItems,
            filteredManagedItems,
            BuildChangeHistory(recipe),
            BuildStateRows(recipe, selectedRecipeFile, allManagedItems));
        bool MatchCell23(ST_RECIPE_CELL cell)
        {
            return cell.CellNo == _selectedCellNo;
        }

        UpdateHoleRows(isCellCategory
            ? BuildHoleRows(
                allManagedItems,
                cells.First(MatchCell23))
            : []);
        ST_DISPLAY_ITEM SelectItem24(ST_RECIPE_PARAM item)
        {
            return new ST_DISPLAY_ITEM(item.Name, $"{item.Value} {item.Unit}".Trim(), item.Range);
        }

        ST_DISPLAY_ITEM SelectItem25(ST_RECIPE_HISTORY item)
        {
            return new ST_DISPLAY_ITEM(item.ItemName, $"{item.OldValue} -> {item.NewValue}", item.OperatorId);
        }

        return new CScreenViewModel(
            EN_MENU.Recipe,
            "RECIPE / MANAGE",
            "Recipe item edit, create, modify, save, delete.",
            [
                new("Recipe Count", recipes.Count.ToString()),
                new("Selected", selectedRecipeFile)
            ],
            [
                new("Managed Items", recipe?.Parameters.Select(SelectItem24).ToArray() ?? []),
                new("Change History", recipe?.History.Select(SelectItem25).ToArray() ?? [])
            ],
            recipe: this);
    }

    private async Task Select(object? parameter)
    {
        var recipeId = GetRecipeIdFromParameter(parameter);

        if (string.IsNullOrWhiteSpace(recipeId))
        {
            return;
        }

        _selectedRecipeIdSetter(recipeId);
        NotifyCommands();
        _setStatusMessage($"Recipe {recipeId}.csv selected.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task SelectCategory(object? parameter)
    {
        if (parameter is not string category || string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        var selectedCategory = category.Trim().ToUpperInvariant();
        _selectedCategorySetter(selectedCategory);
        _selectedGroup = "ALL";
        _setStatusMessage($"Recipe category {selectedCategory} selected.");
        await _refreshCurrentScreen();
    }

    private async Task SelectGroup(object? parameter)
    {
        if (parameter is not string group || string.IsNullOrWhiteSpace(group))
        {
            return;
        }

        _selectedGroup = group.Trim().ToUpperInvariant();
        _setStatusMessage($"Recipe group {_selectedGroup} selected.");
        await _refreshCurrentScreen();
    }

    private async Task SelectCell(object? parameter)
    {
        int EvaluateParameterSwitch1()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case int number:
                    return number;
                case string text when int.TryParse(text, out var number):
                    return number;
                default:
                    return 0;
            }
        }

        var cellNo = EvaluateParameterSwitch1();

        if (cellNo <= 0)
        {
            return;
        }

        _selectedCellNo = cellNo;
        OnPropertyChanged(nameof(CurrentCellIndicatorText));
        OnPropertyChanged(nameof(SelectedCellHoleTitle));
        ApplyPointPatternCommand.NotifyCanExecuteChanged();
        _setStatusMessage($"Recipe Cell{cellNo} selected.");
        await _refreshCurrentScreen();
    }

    private async Task SelectPreviewCell(object? parameter)
    {
        SetCellDetailVisible(true);
        await SelectCell(parameter);
    }

    private void SetCellDetailVisible(bool isVisible)
    {
        if (_isCellDetailVisible == isVisible)
        {
            return;
        }

        _isCellDetailVisible = isVisible;
        OnPropertyChanged(nameof(IsCellDetailVisible));
        OnPropertyChanged(nameof(IsCellPreviewVisible));
    }

    private void SelectHole(object? parameter)
    {
        if (parameter is ST_RECIPE_HOLE_ROW hole)
        {
            SelectedHole = hole;
        }
    }

    private void SetAllOverviewCellsSelected(bool selected)
    {
        foreach (var row in CellOverviewRows)
        {
            row.IsSelected = selected;
        }

        _setStatusMessage(selected ? "All Recipe Cells selected." : "Recipe Cell selection cleared.");
        ApplyPointPatternCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyPointPatternToSelectedCells()
    {
        bool FilterCellNo26(int cellNo)
        {
            return cellNo != _selectedCellNo;
        }

        var targetCellNos = _selectedOverviewCells.Where(FilterCellNo26).ToArray();
        if (targetCellNos.Length == 0)
        {
            _setStatusMessage("Select one or more target Cells. The current Cell is the pattern source.");
            return;
        }

        var sourcePrefix = $"CELL{_selectedCellNo}_";
        bool FilterItem27(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.SourceGroup.Equals("POINT", StringComparison.OrdinalIgnoreCase) &&
                            item.Key.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                            !IsCellPlacementParameter(item.Key[sourcePrefix.Length..]);
        }

        var sourceItems = AllManagedItems
            .Where(FilterItem27)
            .ToArray();

        foreach (var targetCellNo in targetCellNos)
        {
            foreach (var sourceItem in sourceItems)
            {
                var parameterName = sourceItem.Key[sourcePrefix.Length..];
                var targetKey = $"CELL{targetCellNo}_{parameterName}";
                bool MatchItem28(ST_RECIPE_MANAGED_ITEM item)
                {
                    return item.Key.Equals(targetKey, StringComparison.OrdinalIgnoreCase);
                }

                var targetItem = AllManagedItems.FirstOrDefault(MatchItem28);
                if (targetItem is not null)
                {
                    targetItem.Value = sourceItem.Value;
                }
            }
        }

        _setStatusMessage($"Cell{_selectedCellNo} Hole Pattern applied to {targetCellNos.Length} selected Cells.");
        await _refreshCurrentScreen();
    }

    private async Task Save()
    {
        var recipeId = Path.GetFileNameWithoutExtension(SelectedRecipeFile);

        if (string.IsNullOrWhiteSpace(recipeId))
        {
            _setStatusMessage("Recipe save skipped. No recipe is selected.");
            return;
        }

        var recipeParameters = BuildRecipeParameters(recipeId);
        var validationMessage = ValidateRecipeParameters(recipeParameters);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _setStatusMessage(validationMessage);
            return;
        }

        var recipeName = GetEditedRecipeName(recipeParameters, recipeId);

        await _recipeManager.SaveRecipe(new ST_RECIPE_DATA(recipeId, recipeName, recipeParameters, []));

        NotifyCommands();
        _setStatusMessage($"Recipe {recipeId}.csv saved and CSV verified.");
        _showLoadingScreen(EN_MENU.Recipe, "RECIPE");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task Modify()
    {
        var oldRecipeId = GetRecipeIdFromParameter(SelectedRecipeFile);

        if (string.IsNullOrWhiteSpace(oldRecipeId))
        {
            _setStatusMessage("Recipe rename skipped. No recipe is selected.");
            return;
        }

        var recipes = await _recipeManager.LoadRecipes();
        string HandleNewRecipeId29(string value)
        {
            return ValidateRecipeId(NormalizeRecipeIdInput(value), recipes, oldRecipeId);
        }

        var newRecipeId = ShowRecipeNameDialog(
            "Modify Recipe Name",
            "Enter the new recipe name.",
            oldRecipeId,
HandleNewRecipeId29);

        if (newRecipeId is null)
        {
            _setStatusMessage("Recipe rename canceled.");
            return;
        }

        if (newRecipeId.Equals(oldRecipeId, StringComparison.OrdinalIgnoreCase))
        {
            _setStatusMessage("Recipe rename skipped. Name was not changed.");
            return;
        }

        var validationMessage = ValidateRecipeId(newRecipeId, recipes, oldRecipeId);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _setStatusMessage(validationMessage);
            return;
        }

        var recipeParameters = BuildRecipeParameters(oldRecipeId);
        validationMessage = ValidateRecipeParameters(recipeParameters);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _setStatusMessage(validationMessage);
            return;
        }
        bool CheckItem30(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.IsEdited;
        }

        if (AllManagedItems.Any(CheckItem30))
        {
            await _recipeManager.SaveRecipe(new ST_RECIPE_DATA(oldRecipeId, oldRecipeId, recipeParameters, []));
        }

        await _recipeManager.RenameRecipe(oldRecipeId, newRecipeId);

        _selectedRecipeIdSetter(newRecipeId);
        _selectedCategorySetter("ALL");
        _selectedGroup = "ALL";
        NotifyCommands();
        _setStatusMessage($"Recipe {oldRecipeId}.csv renamed to {newRecipeId}.csv and CSV verified.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task Create()
    {
        if (AllManagedItems.Count == 0)
        {
            _setStatusMessage("Recipe create skipped. No source recipe is loaded.");
            return;
        }

        var recipes = await _recipeManager.LoadRecipes();
        string HandleRecipeId31(string value)
        {
            return ValidateRecipeId(NormalizeRecipeIdInput(value), recipes);
        }

        var recipeId = ShowRecipeNameDialog(
            "Create Recipe",
            "Enter the new recipe name.",
            "",
HandleRecipeId31);

        if (recipeId is null)
        {
            _setStatusMessage("Recipe create canceled.");
            return;
        }

        var recipeNameValidationMessage = ValidateRecipeId(recipeId, recipes);

        if (!string.IsNullOrWhiteSpace(recipeNameValidationMessage))
        {
            _setStatusMessage(recipeNameValidationMessage);
            return;
        }

        var recipeParameters = BuildRecipeParameters(recipeId);
        var validationMessage = ValidateRecipeParameters(recipeParameters);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _setStatusMessage(validationMessage);
            return;
        }

        await _recipeManager.SaveRecipe(new ST_RECIPE_DATA(recipeId, recipeId, recipeParameters, []));

        _selectedRecipeIdSetter(recipeId);
        _selectedCategorySetter("ALL");
        _selectedGroup = "ALL";
        NotifyCommands();
        _setStatusMessage($"Recipe {recipeId}.csv created from current recipe and CSV verified.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private async Task Delete()
    {
        var recipeId = GetRecipeIdFromParameter(SelectedRecipeFile);

        if (string.IsNullOrWhiteSpace(recipeId))
        {
            _setStatusMessage("Recipe delete skipped. No recipe is selected.");
            return;
        }

        if (!ConfirmRecipeDelete(recipeId))
        {
            _setStatusMessage($"Recipe {recipeId}.csv delete canceled.");
            return;
        }

        await _recipeManager.DeleteRecipe(recipeId);

        _selectedRecipeIdSetter("");
        _selectedCategorySetter("ALL");
        _selectedGroup = "ALL";
        NotifyCommands();
        _setStatusMessage($"Recipe {recipeId}.csv deleted.");
        _refreshShellStatus();
        await _refreshCurrentScreen();
    }

    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ModifyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ApplyPointPatternCommand.NotifyCanExecuteChanged();
    }

    private void Apply(
        IReadOnlyList<ST_DISPLAY_ITEM> recipeList,
        IReadOnlyList<ST_DISPLAY_ITEM> parameters,
        IReadOnlyList<ST_DISPLAY_ITEM> history,
        IReadOnlyList<ST_DISPLAY_ITEM> actions,
        string selectedRecipeFile,
        IReadOnlyList<ST_RECIPE_FILE> recipeFiles,
        IReadOnlyList<ST_RECIPE_CATEGORY_TAB> itemTabs,
        IReadOnlyList<ST_RECIPE_GROUP_TAB> groupTabs,
        IReadOnlyList<ST_RECIPE_CELL_OVERVIEW_ROW> cellOverviewRows,
        bool isCellCategory,
        ST_RECIPE_LAYOUT_PREVIEW? layoutPreview,
        bool isHeadCategory,
        ST_RECIPE_HEAD_PREVIEW? headPreview,
        IReadOnlyList<ST_SYSTEM_PARAMETER> headPreviewSettings,
        string selectedGroup,
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> allManagedItems,
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        IReadOnlyList<ST_RECIPE_HISTORY_ROW> changeHistory,
        IReadOnlyList<ST_RECIPE_STATE_ROW> stateRows)
    {
        RecipeList = recipeList;
        Parameters = parameters;
        History = history;
        Actions = actions;
        SelectedRecipeFile = selectedRecipeFile;
        RecipeFiles = recipeFiles;
        ItemTabs = itemTabs;
        GroupTabs = groupTabs;
        CellOverviewRows = cellOverviewRows;
        GlassSizeXItem = FindManagedItem(allManagedItems, "GLASS_SIZE_X");
        GlassSizeYItem = FindManagedItem(allManagedItems, "GLASS_SIZE_Y");
        CellCountItem = FindManagedItem(allManagedItems, "CELL_COUNT");
        AkMarginXItem = FindManagedItem(allManagedItems, "AK_MARGIN_X");
        AkMarginYItem = FindManagedItem(allManagedItems, "AK_MARGIN_Y");
        DistortionKeyItems = BuildDistortionKeyItems(allManagedItems);
        IsCellCategory = isCellCategory;
        IsHeadCategory = isHeadCategory;
        CellPreviewImage = layoutPreview?.CellImage;
        CellPreviewLabels = layoutPreview?.CellLabels ?? [];
        HeadPreviewImage = headPreview?.Image;
        HeadPreviewLabels = headPreview?.Labels ?? [];
        HeadPreviewSummaryText = headPreview?.SummaryText ?? "";
        _headPreviewSettings = headPreviewSettings;
        SelectedGroup = selectedGroup;
        TrackPreviewItems(allManagedItems);
        AllManagedItems = allManagedItems;
        ManagedItems = managedItems;
        ChangeHistory = changeHistory;
        StateRows = stateRows;
        NotifyCommands();
    }

    private void TrackPreviewItems(IReadOnlyList<ST_RECIPE_MANAGED_ITEM> items)
    {
        foreach (var item in _previewTrackedItems)
        {
            item.ValueChanged -= OnPreviewItemValueChanged;
        }

        _previewTrackedItems = items;
        foreach (var item in _previewTrackedItems)
        {
            item.ValueChanged += OnPreviewItemValueChanged;
        }
    }

    private void OnPreviewItemValueChanged(object? sender, EventArgs eventArgs)
    {
        if (IsHeadCategory)
        {
            ScheduleHeadPreviewRefresh();
            return;
        }

        if (!IsCellCategory)
        {
            return;
        }

        if (sender is ST_RECIPE_MANAGED_ITEM holeItem && IsHoleOverrideKey(holeItem.Key))
        {
            return;
        }

        if (sender is ST_RECIPE_MANAGED_ITEM item &&
            item.Key.Equals("CELL_COUNT", StringComparison.OrdinalIgnoreCase))
        {
            ScheduleCellStructureRefresh();
        }
        else
        {
            SchedulePreviewRefresh();
        }
    }

    private async void ScheduleCellStructureRefresh()
    {
        _previewRefreshCancellation?.Cancel();
        _previewRefreshCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _previewRefreshCancellation = cancellation;

        try
        {
            await Task.Delay(250, cancellation.Token);
            await _refreshCurrentScreen();
        }
        catch (OperationCanceledException)
        {
            // A newer edit restarted the debounce timer.
        }
    }

    private async void SchedulePreviewRefresh()
    {
        _previewRefreshCancellation?.Cancel();
        _previewRefreshCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _previewRefreshCancellation = cancellation;

        try
        {
            await Task.Delay(400, cancellation.Token);
            var cells = BuildCells(AllManagedItems, GetCellCount(AllManagedItems));
            var preview = BuildLayoutPreview(AllManagedItems, cells, _selectedCellNo);
            CellPreviewImage = preview.CellImage;
            CellPreviewLabels = preview.CellLabels;
            bool MatchCell32(ST_RECIPE_CELL cell)
            {
                return cell.CellNo == _selectedCellNo;
            }

            UpdateHoleRows(BuildHoleRows(
                AllManagedItems,
                cells.First(MatchCell32)));
            OnPropertyChanged(nameof(CellPreviewImage));
            OnPropertyChanged(nameof(CellPreviewLabels));
        }
        catch (OperationCanceledException)
        {
            // A newer edit restarted the debounce timer.
        }
    }

    private async void ScheduleHeadPreviewRefresh()
    {
        _previewRefreshCancellation?.Cancel();
        _previewRefreshCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _previewRefreshCancellation = cancellation;

        try
        {
            await Task.Delay(400, cancellation.Token);
            var preview = BuildHeadPreview(AllManagedItems, _headPreviewSettings, SelectedGroup);
            HeadPreviewImage = preview.Image;
            HeadPreviewLabels = preview.Labels;
            HeadPreviewSummaryText = preview.SummaryText;
            OnPropertyChanged(nameof(HeadPreviewImage));
            OnPropertyChanged(nameof(HeadPreviewLabels));
            OnPropertyChanged(nameof(HeadPreviewSummaryText));
        }
        catch (OperationCanceledException)
        {
            // A newer edit restarted the debounce timer.
        }
    }

    private static ST_RECIPE_DATA? GetSelectedRecipe(
        IReadOnlyList<ST_RECIPE_DATA> recipes,
        string selectedRecipeId)
    {
        if (!string.IsNullOrWhiteSpace(selectedRecipeId))
        {
            bool MatchRecipe33(ST_RECIPE_DATA recipe)
            {
                return recipe.Id.Equals(selectedRecipeId, StringComparison.OrdinalIgnoreCase);
            }

            var selectedRecipe = recipes.FirstOrDefault(MatchRecipe33);

            if (selectedRecipe is not null)
            {
                return selectedRecipe;
            }
        }

        return recipes.FirstOrDefault();
    }

    private static IReadOnlyList<ST_RECIPE_MANAGED_ITEM> GetEditItems(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> loadedItems,
        CMenuRecipe? editScreen,
        string selectedRecipeFile)
    {
        return editScreen is not null &&
            editScreen.SelectedRecipeFile.Equals(selectedRecipeFile, StringComparison.OrdinalIgnoreCase) &&
            editScreen.AllManagedItems.Count > 0
                ? editScreen.AllManagedItems
                : loadedItems;
    }

    private static IReadOnlyList<ST_DISPLAY_ITEM> BuildActions()
    {
        return
        [
            new("Create", "Ready"),
            new("Save", "Ready"),
            new("Delete", "Ready"),
            new("Modify", "Rename")
        ];
    }

    private static string GetRecipeFileName(ST_RECIPE_DATA? recipe)
    {
        return recipe is null ? "" : $"{recipe.Id}.csv";
    }

    private static IReadOnlyList<ST_RECIPE_FILE> BuildRecipeFiles(
        IReadOnlyList<ST_RECIPE_DATA> recipes,
        ST_RECIPE_DATA? selectedRecipe)
    {
        ST_RECIPE_FILE SelectRecipe34(ST_RECIPE_DATA recipe, int index)
        {
            return new ST_RECIPE_FILE(
                            (index + 1).ToString("00"),
                            GetRecipeFileName(recipe),
                            selectedRecipe is not null && recipe.Id.Equals(selectedRecipe.Id, StringComparison.OrdinalIgnoreCase));
        }

        return recipes
            .Select(SelectRecipe34)
            .ToArray();
    }

    private static IReadOnlyList<ST_RECIPE_MANAGED_ITEM> BuildManagedItems(ST_RECIPE_DATA? recipe)
    {
        if (recipe is null)
        {
            return [];
        }
        bool FilterParameter35(ST_RECIPE_PARAM parameter)
        {
            return parameter.Use && parameter.Show;
        }

        List<CIndexedRecipeParameter> sortedParameters = new List<CIndexedRecipeParameter>();
        int filteredIndex = 0;
        foreach (ST_RECIPE_PARAM parameter in recipe.Parameters)
        {
            if (!FilterParameter35(parameter))
            {
                continue;
            }

            sortedParameters.Add(new CIndexedRecipeParameter(parameter, filteredIndex));
            filteredIndex++;
        }

        sortedParameters.Sort(CompareIndexedRecipeParameters);

        List<ST_RECIPE_MANAGED_ITEM> managedItems = new List<ST_RECIPE_MANAGED_ITEM>();
        foreach (CIndexedRecipeParameter indexedParameter in sortedParameters)
        {
            ST_RECIPE_PARAM parameter = indexedParameter.Parameter;
            string category = NormalizeRecipeText(parameter.Tab, "COMMON");
            string group = NormalizeRecipeText(parameter.Group, category);

            managedItems.Add(new ST_RECIPE_MANAGED_ITEM(
                category,
                group,
                parameter.Name,
                parameter.Value,
                NormalizeUnit(parameter.Unit),
                parameter.Description,
                GetValueState(parameter),
                parameter.Key,
                group,
                parameter.DataType,
                parameter.ChangeLimit,
                parameter.Min,
                parameter.Max));
        }

        return managedItems.ToArray();
    }

    private static IReadOnlyList<string> BuildCategories(IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems)
    {
        string SelectItem36(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Category;
        }

        bool FilterCategory37(string category)
        {
            return !string.IsNullOrWhiteSpace(category);
        }

        var categories = managedItems
            .Select(SelectItem36)
            .Where(FilterCategory37)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = categories.ToList();
        bool CheckCategory38(string category)
        {
            return category.Equals("CELL", StringComparison.OrdinalIgnoreCase);
        }

        if (!result.Any(CheckCategory38))
        {
            result.Add("CELL");
        }

        return result;
    }

    private static int GetCellCount(IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems)
    {
        bool MatchItem39(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.Equals("CELL_COUNT", StringComparison.OrdinalIgnoreCase);
        }

        var countItem = managedItems.FirstOrDefault(MatchItem39);

        return countItem is not null &&
            int.TryParse(countItem.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                ? Math.Max(1, count)
                : 1;
    }

    private static IReadOnlyList<ST_RECIPE_CELL> BuildCells(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        int cellCount)
    {
        var akMarginX = ReadManagedDouble(managedItems, "AK_MARGIN_X", 55.0);
        var akMarginY = ReadManagedDouble(managedItems, "AK_MARGIN_Y", 45.0);
        Dictionary<int, IReadOnlyList<ST_RECIPE_MANAGED_ITEM>> itemsByCellNo =
            new Dictionary<int, IReadOnlyList<ST_RECIPE_MANAGED_ITEM>>();
        Dictionary<int, List<ST_RECIPE_MANAGED_ITEM>> itemListsByCellNo =
            new Dictionary<int, List<ST_RECIPE_MANAGED_ITEM>>();

        foreach (ST_RECIPE_MANAGED_ITEM item in managedItems)
        {
            int cellNo = TryGetCellNo(item.Key);
            if (cellNo <= 0)
            {
                continue;
            }

            if (!itemListsByCellNo.TryGetValue(cellNo, out List<ST_RECIPE_MANAGED_ITEM>? cellItems))
            {
                cellItems = new List<ST_RECIPE_MANAGED_ITEM>();
                itemListsByCellNo.Add(cellNo, cellItems);
            }

            cellItems.Add(item);
        }

        foreach (KeyValuePair<int, List<ST_RECIPE_MANAGED_ITEM>> entry in itemListsByCellNo)
        {
            itemsByCellNo.Add(entry.Key, entry.Value.ToArray());
        }
        ST_RECIPE_CELL SelectCellNo40(int cellNo)
        {
            int GetItemSortKey1(ST_RECIPE_MANAGED_ITEM item)
            {
                return GetCellItemGroupOrder(item.SourceGroup);
            }

            var items = (itemsByCellNo.GetValueOrDefault(cellNo) ?? [])
                .OrderBy(GetItemSortKey1)
                .ToArray();
            var result = CCellPointCalculator.Calculate(new ST_CELL_POINT_INPUT(
                cellNo,
                ReadCellDouble(items, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_X"),
                ReadCellDouble(items, $"CELL{cellNo}_ALIGN_TO_1ST_PIXEL_Y"),
                ReadCellDouble(items, $"CELL{cellNo}_ROTATION"),
                ReadCellInt(items, $"CELL{cellNo}_NUM_OF_PIXEL_X"),
                ReadCellInt(items, $"CELL{cellNo}_NUM_OF_PIXEL_Y"),
                ReadCellDouble(items, $"CELL{cellNo}_PITCH_X"),
                ReadCellDouble(items, $"CELL{cellNo}_PITCH_Y"),
                akMarginX,
                akMarginY));

            return new ST_RECIPE_CELL(
                cellNo,
                items,
                result.Points);
        }
        return Enumerable.Range(1, cellCount)
            .Select(SelectCellNo40)
            .ToArray();
    }

    private static int CompareIndexedRecipeParameters(
        CIndexedRecipeParameter left,
        CIndexedRecipeParameter right)
    {
        int leftOrder = left.Parameter.DisplayOrder <= 0
            ? int.MaxValue
            : left.Parameter.DisplayOrder;
        int rightOrder = right.Parameter.DisplayOrder <= 0
            ? int.MaxValue
            : right.Parameter.DisplayOrder;

        int orderComparison = leftOrder.CompareTo(rightOrder);
        if (orderComparison != 0)
        {
            return orderComparison;
        }

        return left.Index.CompareTo(right.Index);
    }

    private sealed class CIndexedRecipeParameter
    {
        public CIndexedRecipeParameter(ST_RECIPE_PARAM parameter, int index)
        {
            Parameter = parameter;
            Index = index;
        }

        public ST_RECIPE_PARAM Parameter { get; }
        public int Index { get; }
    }

    private static int GetCellItemGroupOrder(string group)
    {
        if (group.Equals("CELL", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (group.Equals("POINT", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static int ReadCellInt(IReadOnlyList<ST_RECIPE_MANAGED_ITEM> items, string key)
    {
        bool MatchItem41(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var value = items.FirstOrDefault(MatchItem41)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static double ReadCellDouble(IReadOnlyList<ST_RECIPE_MANAGED_ITEM> items, string key)
    {
        bool MatchItem42(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var value = items.FirstOrDefault(MatchItem42)?.Value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0.0;
    }

    private static int TryGetCellNo(string key)
    {
        if (!key.StartsWith("CELL", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var separatorIndex = key.IndexOf('_', 4);
        return separatorIndex > 4 &&
            int.TryParse(key.AsSpan(4, separatorIndex - 4), NumberStyles.None, CultureInfo.InvariantCulture, out var cellNo)
                ? cellNo
                : 0;
    }

    private static bool IsMovedToCellTab(ST_RECIPE_MANAGED_ITEM item)
    {
        return TryGetCellNo(item.Key) > 0 ||
            CellParameterKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase) ||
            GlassSettingKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static ST_RECIPE_MANAGED_ITEM? FindManagedItem(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        string key)
    {
        bool MatchItem43(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        return managedItems.FirstOrDefault(MatchItem43);
    }

    private static IReadOnlyList<ST_RECIPE_DISTORTION_KEY_ITEM> BuildDistortionKeyItems(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems)
    {
        ST_RECIPE_DISTORTION_KEY_ITEM SelectKeyNo44(int keyNo)
        {
            return new ST_RECIPE_DISTORTION_KEY_ITEM(
                            keyNo,
                            FindManagedItem(managedItems, $"DISTORTION_KEY{keyNo}_X"),
                            FindManagedItem(managedItems, $"DISTORTION_KEY{keyNo}_Y"));
        }

        return Enumerable.Range(1, 6)
            .Select(SelectKeyNo44)
            .ToArray();
    }

    private static IReadOnlyList<ST_RECIPE_MANAGED_ITEM> EnsureCellPointItems(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        int cellCount)
    {
        var result = managedItems.ToList();
        ST_RECIPE_MANAGED_ITEM? SelectKey45(string key)
        {
            bool MatchItem2(ST_RECIPE_MANAGED_ITEM item)
            {
                return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
            }

            return managedItems.FirstOrDefault(MatchItem2);
        }

        bool FilterItem46(ST_RECIPE_MANAGED_ITEM? item)
        {
            return item is not null;
        }

        string HandleTemplates47(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key;
        }

        var templates = CellParameterKeys
            .Select(SelectKey45)
            .Where(FilterItem46)
            .Cast<ST_RECIPE_MANAGED_ITEM>()
            .ToDictionary(HandleTemplates47, StringComparer.OrdinalIgnoreCase);

        foreach (var cellNo in Enumerable.Range(1, cellCount))
        {
            foreach (var parameterKey in CellParameterKeys)
            {
                var scopedKey = $"CELL{cellNo}_{GetCellScopedParameterName(parameterKey)}";
                bool CheckItem48(ST_RECIPE_MANAGED_ITEM item)
                {
                    return item.Key.Equals(scopedKey, StringComparison.OrdinalIgnoreCase);
                }

                if (result.Any(CheckItem48) ||
                    !templates.TryGetValue(parameterKey, out var template))
                {
                    continue;
                }

                result.Add(new ST_RECIPE_MANAGED_ITEM(
                    "CELL",
                    GetCellParameterGroup(parameterKey),
                    template.Item,
                    template.Value,
                    template.Unit,
                    template.Description,
                    template.ValueState,
                    scopedKey,
                    GetCellParameterGroup(parameterKey),
                    template.DataType,
                    template.ChangeLimit,
                    template.Min,
                    template.Max));
            }
        }

        return result;
    }

    private static string GetCellParameterGroup(string parameterKey)
    {
        return parameterKey.StartsWith("CELL_", StringComparison.OrdinalIgnoreCase)
            ? "CELL"
            : "POINT";
    }

    private static string GetCellScopedParameterName(string parameterKey)
    {
        return parameterKey.StartsWith("CELL_", StringComparison.OrdinalIgnoreCase)
            ? parameterKey[5..]
            : parameterKey;
    }

    private static bool IsCellPlacementParameter(string parameterName)
    {
        return parameterName.Equals("ALIGN_TO_1ST_PIXEL_X", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Equals("ALIGN_TO_1ST_PIXEL_Y", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Equals("ROTATION", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<ST_RECIPE_CELL_OVERVIEW_ROW> BuildCellOverviewRows(IReadOnlyList<ST_RECIPE_CELL> cells)
    {
        ST_RECIPE_CELL_OVERVIEW_ROW SelectCell49(ST_RECIPE_CELL cell)
        {
            void HandleCellNoCallback3(int cellNo, bool selected)
            {
                if (selected)
                {
                    _selectedOverviewCells.Add(cellNo);
                }
                else
                {
                    _selectedOverviewCells.Remove(cellNo);
                }
                ApplyPointPatternCommand.NotifyCanExecuteChanged();
            }
            return new ST_RECIPE_CELL_OVERVIEW_ROW(
                        cell,
                        cell.CellNo == _selectedCellNo,
                        _selectedOverviewCells.Contains(cell.CellNo),
HandleCellNoCallback3);
        }

        return cells.Select(SelectCell49).ToArray();
    }

    private static ST_RECIPE_LAYOUT_PREVIEW BuildLayoutPreview(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        IReadOnlyList<ST_RECIPE_CELL> cells,
        int selectedCellNo)
    {
        const double canvasWidth = 860.0;
        const double canvasHeight = 430.0;
        const double maxLeft = 44.0;
        const double maxTop = 50.0;
        const double maxWidth = 772.0;
        const double maxHeight = 340.0;
        var glassWidth = ReadManagedDouble(managedItems, "GLASS_SIZE_X", 500.0);
        var glassHeight = ReadManagedDouble(managedItems, "GLASS_SIZE_Y", 300.0);
        var akMarginX = ReadManagedDouble(managedItems, "AK_MARGIN_X", 55.0);
        var akMarginY = ReadManagedDouble(managedItems, "AK_MARGIN_Y", 45.0);
        double? HandleDistortionKeys50(string key)
        {
            return ReadManagedNullableDouble(managedItems, key);
        }

        var distortionKeys = CCellPreviewDrawing.CreateDistortionKeyPreviews(
            glassWidth,
            glassHeight,
            akMarginX,
            akMarginY,
HandleDistortionKeys50);

        if (glassWidth <= 0 || glassHeight <= 0)
        {
            return new ST_RECIPE_LAYOUT_PREVIEW(
                null,
                []);
        }

        var scale = Math.Min(maxWidth / glassWidth, maxHeight / glassHeight);
        var frameWidth = glassWidth * scale;
        var frameHeight = glassHeight * scale;
        var frameLeft = maxLeft + (maxWidth - frameWidth) / 2.0;
        var frameTop = maxTop + (maxHeight - frameHeight) / 2.0;
        var frame = new ST_GLASS_PREVIEW_FRAME(frameLeft, frameTop, frameWidth, frameHeight);
        var drawing = new DrawingGroup();
        var outsidePixels = new HashSet<long>();
        var cellLabels = new List<ST_CELL_PREVIEW_LABEL>();

        using (var context = drawing.Open())
        {
            // Keep the DrawingImage coordinate space identical to Main's 860x430 Canvas.
            // Without this transparent frame WPF stretches the point-only content bounds.
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                null,
                new Rect(0, 0, canvasWidth, canvasHeight));

            var outsideGeometry = new StreamGeometry();

            foreach (var cell in cells)
            {
                var geometry = new StreamGeometry();
                var pixels = new HashSet<long>();
                var pixelSize = Math.Max(0.0, ReadCellDouble(cell.Items, $"CELL{cell.CellNo}_PIXEL_SIZE"));
                var pixelRadius = pixelSize / 2.0;
                var previewPointSize = Math.Clamp(pixelSize * scale, 1.5, 12.0);
                using (var geometryContext = geometry.Open())
                {
                    foreach (var point in cell.Points)
                    {
                        var isInside = point.X - pixelRadius >= 0 && point.X + pixelRadius <= glassWidth &&
                            point.Y - pixelRadius >= 0 && point.Y + pixelRadius <= glassHeight;
                        var canvasX = frameLeft + (point.X * scale);
                        var canvasY = frameTop + (point.Y * scale);
                        var pixelX = (int)Math.Round(canvasX);
                        var pixelY = (int)Math.Round(canvasY);
                        var pixelKey = ((long)pixelX << 32) | (uint)pixelY;

                        if (!isInside)
                        {
                            outsidePixels.Add(pixelKey);
                            continue;
                        }

                        if (pixels.Add(pixelKey))
                        {
                            AddPointCircle(geometryContext, pixelX, pixelY, previewPointSize);
                        }
                    }
                }

                geometry.Freeze();
                var isSelectedCell = selectedCellNo > 0 && cell.CellNo == selectedCellNo;
                var cellBrush = CMenuMain.CreateHeadBrush(
                    ((cell.CellNo - 1) % 8) + 1,
                    (byte)(selectedCellNo <= 0 || isSelectedCell ? 230 : 105));
                context.DrawGeometry(
                    cellBrush,
                    null,
                    geometry);

                var displayedHoleRadius = previewPointSize / (2.0 * scale);
                var boundaryPadding = Math.Max(pixelRadius, displayedHoleRadius) + (4.0 / scale);
                var boundary = BuildCellBoundaryGeometry(
                    cell,
                    frameLeft,
                    frameTop,
                    scale,
                    boundaryPadding,
                    akMarginX,
                    akMarginY);
                if (boundary is not null)
                {
                    // Cell Size is not defined. The point-pattern bounds are used only
                    // to anchor the label and are intentionally not drawn as a boundary.
                    var label = CCellPreviewDrawing.CreateCellLabel(
                        cell.CellNo,
                        boundary.Bounds,
                        canvasWidth,
                        canvasHeight,
                        isSelectedCell);
                    if (label is not null)
                    {
                        cellLabels.Add(label);
                    }
                }
            }

            using (var outsideContext = outsideGeometry.Open())
            {
                foreach (var pixel in outsidePixels)
                {
                    var x = (int)(pixel >> 32);
                    var y = (int)pixel;
                    AddPointCircle(outsideContext, x, y, 4.0);
                }
            }

            outsideGeometry.Freeze();
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(248, 113, 113)), null, outsideGeometry);

            CCellPreviewDrawing.DrawAlignKeys(
                context,
                frame,
                glassWidth,
                glassHeight,
                akMarginX,
                akMarginY);
            CCellPreviewDrawing.DrawDistortionKeys(
                context,
                frame,
                glassWidth,
                glassHeight,
                distortionKeys);

        }

        drawing.Freeze();
        var paddingX = frameWidth * 0.03;
        var paddingY = Math.Max(22.0, frameHeight * 0.03);
        var cellRect = new Rect(0, 0, frameWidth + (paddingX * 2.0), frameHeight + (paddingY * 2.0));
        var glassRect = new Rect(paddingX, paddingY, frameWidth, frameHeight);
        ST_CELL_PREVIEW_LABEL SelectLabel51(ST_CELL_PREVIEW_LABEL label)
        {
            return label with
            {
                CanvasCenterX = label.CanvasCenterX - frameLeft + paddingX,
                CanvasCenterY = label.CanvasCenterY - frameTop + paddingY,
                DesignWidth = cellRect.Width,
                DesignHeight = cellRect.Height
            };
        }

        var translatedCellLabels = cellLabels
            .Select(SelectLabel51)
            .ToArray();
        var cellDrawing = new DrawingGroup();
        using (var cellContext = cellDrawing.Open())
        {
            cellContext.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                null,
                cellRect);
            cellContext.DrawRectangle(
                null,
                new Pen(new SolidColorBrush(Color.FromRgb(102, 136, 164)), 1.8),
                glassRect);
            cellContext.PushClip(new RectangleGeometry(cellRect));
            cellContext.PushTransform(new TranslateTransform(
                -(frameLeft - paddingX),
                -(frameTop - paddingY)));
            cellContext.DrawDrawing(drawing);
            cellContext.Pop();
            cellContext.Pop();
            DrawPreviewAxisIndicator(cellContext, glassRect);
        }
        cellDrawing.Freeze();
        var cellImage = new DrawingImage(cellDrawing);
        cellImage.Freeze();
        return new ST_RECIPE_LAYOUT_PREVIEW(
            cellImage,
            translatedCellLabels);
    }

    private static ST_RECIPE_HEAD_PREVIEW BuildHeadPreview(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings,
        string selectedGroup)
    {
        const double maxWidth = 772.0;
        const double maxHeight = 340.0;
        const double paddingX = 44.0;
        const double paddingTop = 42.0;
        const double paddingBottom = 28.0;
        var glassWidth = ReadManagedDouble(managedItems, "GLASS_SIZE_X", 500.0);
        var glassHeight = ReadManagedDouble(managedItems, "GLASS_SIZE_Y", 300.0);
        var akMarginX = ReadManagedDouble(managedItems, "AK_MARGIN_X", 55.0);
        var akMarginY = ReadManagedDouble(managedItems, "AK_MARGIN_Y", 45.0);
        double? HandleDistortionKeys52(string key)
        {
            return ReadManagedNullableDouble(managedItems, key);
        }

        var distortionKeys = CCellPreviewDrawing.CreateDistortionKeyPreviews(
            glassWidth,
            glassHeight,
            akMarginX,
            akMarginY,
HandleDistortionKeys52);
        var headCount = Math.Clamp(ReadManagedInt(managedItems, "HEAD_COUNT", 8), 1, 8);
        var head1AkPositionX = ReadSettingDouble(settings, "H01_AK_POSITION_X", -5.0);
        var headGapX = ReadSettingDouble(settings, "HeadGapX", 200.0);
        var headGapY = ReadSettingDouble(settings, "HeadGapY", 0.0);

        if (glassWidth <= 0.0 || glassHeight <= 0.0)
        {
            return new ST_RECIPE_HEAD_PREVIEW(
                null,
                [],
                "Glass size is not valid.");
        }

        var scale = Math.Min(maxWidth / glassWidth, maxHeight / glassHeight);
        var frameWidth = glassWidth * scale;
        var frameHeight = glassHeight * scale;
        var previewWidth = frameWidth + (paddingX * 2.0);
        var previewHeight = frameHeight + paddingTop + paddingBottom;
        var previewRect = new Rect(0.0, 0.0, previewWidth, previewHeight);
        var glassRect = new Rect(paddingX, paddingTop, frameWidth, frameHeight);
        var frame = new ST_GLASS_PREVIEW_FRAME(glassRect.Left, glassRect.Top, glassRect.Width, glassRect.Height);
        var labels = new List<ST_HEAD_COVERAGE_PREVIEW_LABEL>();
        var drawing = new DrawingGroup();

        using (var context = drawing.Open())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                null,
                previewRect);
            context.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(17, 24, 32)),
                null,
                glassRect);

            DrawHeadPreviewScale(context, glassRect);
            DrawAkCenterGuides(
                context,
                glassRect,
                glassWidth,
                glassHeight,
                akMarginX,
                akMarginY,
                scale);

            for (var headNo = 1; headNo <= headCount; headNo++)
            {
                var defaultOffsetX = ReadManagedDouble(
                    managedItems,
                    $"H{headNo:00}_DEFAULT_OFFSET_X",
                    0.0);
                var defaultOffsetY = ReadManagedDouble(
                    managedItems,
                    $"H{headNo:00}_DEFAULT_OFFSET_Y",
                    0.0);
                var centerX = akMarginX + head1AkPositionX + ((headNo - 1) * headGapX) + defaultOffsetX;
                var laneY = CalculateHeadPreviewLaneY(glassHeight, headGapY, headNo) + defaultOffsetY;
                var canvasCenterX = glassRect.Left + (centerX * scale);
                var canvasCenterY = glassRect.Top + (laneY * scale);

                labels.Add(CreateHeadPreviewLabel(
                    headNo,
                    canvasCenterX,
                    canvasCenterY,
                    previewWidth,
                    previewHeight,
                    IsSelectedHeadPreviewGroup(selectedGroup, headNo)));
            }

            context.DrawRectangle(
                null,
                new Pen(new SolidColorBrush(Color.FromRgb(102, 136, 164)), 1.8),
                glassRect);
            CCellPreviewDrawing.DrawAlignKeys(
                context,
                frame,
                glassWidth,
                glassHeight,
                akMarginX,
                akMarginY);
            CCellPreviewDrawing.DrawDistortionKeys(
                context,
                frame,
                glassWidth,
                glassHeight,
                distortionKeys);
            DrawPreviewAxisIndicator(context, glassRect);
        }

        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        var summaryText =
            $"Glass {FormatPreviewDouble(glassWidth)} x {FormatPreviewDouble(glassHeight)} mm / " +
            $"AK To Head1 Center {FormatPreviewDouble(head1AkPositionX)} mm / " +
            $"Head Gap X {FormatPreviewDouble(headGapX)} mm / " +
            $"Head Gap Y {FormatPreviewDouble(headGapY)} mm";
        return new ST_RECIPE_HEAD_PREVIEW(
            image,
            labels,
            summaryText);
    }

    private static double CalculateHeadPreviewLaneY(
        double glassHeight,
        double headGapY,
        int headNo)
    {
        var centerY = glassHeight / 2.0;
        var oddLaneY = centerY - (headGapY / 2.0);
        var evenLaneY = oddLaneY + headGapY;

        return headNo % 2 == 0
            ? evenLaneY
            : oddLaneY;
    }

    private static bool IsSelectedHeadPreviewGroup(
        string selectedGroup,
        int headNo)
    {
        return selectedGroup.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
            selectedGroup.Equals($"H{headNo:00}", StringComparison.OrdinalIgnoreCase);
    }

    private static void DrawHeadPreviewScale(
        DrawingContext context,
        Rect glassRect)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(72, 102, 136, 164));
        gridBrush.Freeze();
        var gridPen = new Pen(gridBrush, 0.8);
        gridPen.Freeze();

        for (var index = 1; index < 6; index++)
        {
            var x = glassRect.Left + (glassRect.Width * index / 6.0);
            context.DrawLine(
                gridPen,
                new Point(x, glassRect.Top),
                new Point(x, glassRect.Bottom));
        }
    }

    private static void DrawAkCenterGuides(
        DrawingContext context,
        Rect glassRect,
        double glassWidth,
        double glassHeight,
        double akMarginX,
        double akMarginY,
        double scale)
    {
        var guideBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        guideBrush.Freeze();
        var guidePen = new Pen(guideBrush, 1.35)
        {
            DashStyle = DashStyles.Dash,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        guidePen.Freeze();

        var centerCanvasX = glassRect.Left + ((glassWidth / 2.0) * scale);
        var centerCanvasY = glassRect.Top + ((glassHeight / 2.0) * scale);

        context.DrawLine(
            guidePen,
            new Point(centerCanvasX, glassRect.Top),
            new Point(centerCanvasX, glassRect.Bottom));
        context.DrawLine(
            guidePen,
            new Point(glassRect.Left, centerCanvasY),
            new Point(glassRect.Right, centerCanvasY));

        var akCenterDistanceX = Math.Abs((glassWidth / 2.0) - akMarginX);
        var akCenterDistanceY = Math.Abs((glassHeight / 2.0) - akMarginY);
        DrawAkCenterGuideText(
            context,
            $"X {FormatPreviewDouble(akCenterDistanceX)} mm",
            new Point(centerCanvasX, Math.Max(6.0, glassRect.Top - 26.0)),
            true,
            guideBrush);
        DrawAkCenterGuideText(
            context,
            $"Y {FormatPreviewDouble(akCenterDistanceY)} mm",
            new Point(glassRect.Left + 8.0, centerCanvasY - 26.0),
            false,
            guideBrush);
    }

    private static void DrawAkCenterGuideText(
        DrawingContext context,
        string text,
        Point anchor,
        bool centerAligned,
        Brush brush)
    {
        var label = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            12.0,
            brush,
            1.0);
        var left = centerAligned
            ? anchor.X - (label.Width / 2.0)
            : anchor.X;

        context.DrawText(label, new Point(left, anchor.Y));
    }

    private static void DrawPreviewAxisIndicator(
        DrawingContext context,
        Rect glassRect)
    {
        var axisBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        axisBrush.Freeze();
        var axisPen = new Pen(axisBrush, 2.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        axisPen.Freeze();

        var origin = new Point(
            Math.Max(4.0, glassRect.Left - 8.0),
            Math.Max(4.0, glassRect.Top - 12.0));
        var xEnd = new Point(origin.X + 46.0, origin.Y);
        var yEnd = new Point(origin.X, origin.Y + 42.0);

        context.DrawLine(axisPen, origin, xEnd);
        context.DrawLine(axisPen, new Point(xEnd.X - 8.0, xEnd.Y - 4.0), xEnd);
        context.DrawLine(axisPen, new Point(xEnd.X - 8.0, xEnd.Y + 4.0), xEnd);

        context.DrawLine(axisPen, origin, yEnd);
        context.DrawLine(axisPen, new Point(yEnd.X - 4.0, yEnd.Y - 8.0), yEnd);
        context.DrawLine(axisPen, new Point(yEnd.X + 4.0, yEnd.Y - 8.0), yEnd);

        DrawPreviewAxisText(context, "X+", new Point(xEnd.X + 4.0, Math.Max(2.0, xEnd.Y - 10.0)), axisBrush);
        DrawPreviewAxisText(context, "Y+", new Point(yEnd.X + 4.0, yEnd.Y - 4.0), axisBrush);
    }

    private static void DrawPreviewAxisText(
        DrawingContext context,
        string text,
        Point point,
        Brush brush)
    {
        var label = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            13.0,
            brush,
            1.0);

        context.DrawText(label, point);
    }

    private static ST_HEAD_COVERAGE_PREVIEW_LABEL CreateHeadPreviewLabel(
        int headNo,
        double centerX,
        double centerY,
        double designWidth,
        double designHeight,
        bool isSelected)
    {
        var label = new FormattedText(
            $"H{headNo:00}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            12.0,
            Brushes.White,
            1.0);
        var badgeWidth = Math.Max(48.0, label.Width + 18.0);

        return new ST_HEAD_COVERAGE_PREVIEW_LABEL(
            headNo,
            centerX,
            centerY,
            badgeWidth,
            designWidth,
            designHeight,
            isSelected);
    }

    private static StreamGeometry? BuildCellBoundaryGeometry(
        ST_RECIPE_CELL cell,
        double frameLeft,
        double frameTop,
        double scale,
        double boundaryPadding,
        double originX,
        double originY)
    {
        var countX = ReadCellInt(cell.Items, $"CELL{cell.CellNo}_NUM_OF_PIXEL_X");
        var countY = ReadCellInt(cell.Items, $"CELL{cell.CellNo}_NUM_OF_PIXEL_Y");
        var pitchX = ReadCellDouble(cell.Items, $"CELL{cell.CellNo}_PITCH_X");
        var pitchY = ReadCellDouble(cell.Items, $"CELL{cell.CellNo}_PITCH_Y");
        if (countX <= 0 || countY <= 0 || pitchX < 0 || pitchY < 0)
        {
            return null;
        }

        var firstX = originX + ReadCellDouble(cell.Items, $"CELL{cell.CellNo}_ALIGN_TO_1ST_PIXEL_X");
        var firstY = originY + ReadCellDouble(cell.Items, $"CELL{cell.CellNo}_ALIGN_TO_1ST_PIXEL_Y");
        var radians = ReadCellDouble(cell.Items, $"CELL{cell.CellNo}_ROTATION") * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var localMaxX = ((countX - 1) * pitchX) + boundaryPadding;
        var localMaxY = ((countY - 1) * pitchY) + boundaryPadding;
        var localCorners = new[]
        {
            new Point(-boundaryPadding, -boundaryPadding),
            new Point(localMaxX, -boundaryPadding),
            new Point(localMaxX, localMaxY),
            new Point(-boundaryPadding, localMaxY)
        };
        Point SelectLocal53(Point local)
        {
            var x = firstX + (local.X * cos) - (local.Y * sin);
            var y = firstY + (local.X * sin) + (local.Y * cos);
            return new Point(frameLeft + (x * scale), frameTop + (y * scale));
        }
        var canvasCorners = localCorners.Select(SelectLocal53).ToArray();

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(canvasCorners[0], false, true);
            geometryContext.PolyLineTo(canvasCorners.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static void AddPointCircle(StreamGeometryContext context, double x, double y, double size)
    {
        var radius = size / 2.0;
        var control = radius * 0.5522847498;
        context.BeginFigure(new Point(x + radius, y), true, true);
        context.BezierTo(
            new Point(x + radius, y + control),
            new Point(x + control, y + radius),
            new Point(x, y + radius), true, false);
        context.BezierTo(
            new Point(x - control, y + radius),
            new Point(x - radius, y + control),
            new Point(x - radius, y), true, false);
        context.BezierTo(
            new Point(x - radius, y - control),
            new Point(x - control, y - radius),
            new Point(x, y - radius), true, false);
        context.BezierTo(
            new Point(x + control, y - radius),
            new Point(x + radius, y - control),
            new Point(x + radius, y), true, false);
    }

    private IReadOnlyList<ST_RECIPE_HOLE_ROW> BuildHoleRows(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        ST_RECIPE_CELL cell)
    {
        var glassWidth = ReadManagedDouble(managedItems, "GLASS_SIZE_X", 500.0);
        var glassHeight = ReadManagedDouble(managedItems, "GLASS_SIZE_Y", 300.0);
        var holeRadius = Math.Max(
            0.0,
            ReadCellDouble(cell.Items, $"CELL{cell.CellNo}_PIXEL_SIZE")) / 2.0;
        bool FilterItem54(ST_RECIPE_MANAGED_ITEM item)
        {
            return TryGetCellNo(item.Key) == cell.CellNo &&
                            IsHoleOverrideKey(item.Key);
        }

        string HandleOverrideValues55(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key;
        }

        string HandleOverrideValues56(IGrouping<string, ST_RECIPE_MANAGED_ITEM> group)
        {
            return group.Key;
        }

        string HandleOverrideValues57(IGrouping<string, ST_RECIPE_MANAGED_ITEM> group)
        {
            return group.Last().Value;
        }

        var overrideValues = managedItems
            .Where(FilterItem54)
            .GroupBy(HandleOverrideValues55, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
HandleOverrideValues56,
HandleOverrideValues57,
                StringComparer.OrdinalIgnoreCase);
        ST_RECIPE_HOLE_ROW SelectPoint58(ST_CELL_DRILL_POINT point)
        {
            var isInsideGlass = point.X - holeRadius >= 0.0 &&
                point.X + holeRadius <= glassWidth &&
                point.Y - holeRadius >= 0.0 &&
                point.Y + holeRadius <= glassHeight;
            int MaxCandidateCallback4(ST_CELL_DRILL_POINT candidate)
            {
                return candidate.Column;
            }

            var holeName = CReviewHoleNameFormatter.ToMatrixName(
                point.PointNo,
                Math.Max(1, cell.Points.Max(MaxCandidateCallback4) + 1));
            var recipeKeyPrefix = $"CELL{cell.CellNo}_{holeName}_RECIPE_OFFSET_";
            void HandleParameterNameCallback5(string parameterName, string value)
            {
                SetHoleOverrideValue(
                                    cell.CellNo,
                                    point.PointNo,
                                    holeName,
                                    parameterName,
                                    value);
            }

            return new ST_RECIPE_HOLE_ROW(
                point.PointNo,
                point.Row + 1,
                point.Column + 1,
                isInsideGlass,
                overrideValues.GetValueOrDefault($"{recipeKeyPrefix}X", "0"),
                overrideValues.GetValueOrDefault($"{recipeKeyPrefix}Y", "0"),
HandleParameterNameCallback5);
        }
        return cell.Points
            .Select(SelectPoint58)
            .ToArray();
    }

    private void UpdateHoleRows(IReadOnlyList<ST_RECIPE_HOLE_ROW> rows)
    {
        HoleRows = rows;
        int SelectRow59(ST_RECIPE_HOLE_ROW row)
        {
            return row.Column;
        }

        int GetColumnSortKey60(int column)
        {
            return column;
        }

        HoleMatrixColumnHeaders = rows
            .Select(SelectRow59)
            .Distinct()
            .OrderBy(GetColumnSortKey60)
            .Select(ToColumnLetter)
            .ToArray();
        int GroupByRowCallback61(ST_RECIPE_HOLE_ROW row)
        {
            return row.Row;
        }

        int GetGroupSortKey62(IGrouping<int, ST_RECIPE_HOLE_ROW> group)
        {
            return group.Key;
        }

        ST_RECIPE_HOLE_MATRIX_ROW SelectGroup63(IGrouping<int, ST_RECIPE_HOLE_ROW> group)
        {
            int GetRowSortKey6(ST_RECIPE_HOLE_ROW row)
            {
                return row.Column;
            }

            return new ST_RECIPE_HOLE_MATRIX_ROW(
                            group.Key,
                            group.OrderBy(GetRowSortKey6).ToArray());
        }

        HoleMatrixRows = rows
            .GroupBy(GroupByRowCallback61)
            .OrderBy(GetGroupSortKey62)
            .Select(SelectGroup63)
            .ToArray();
        OnPropertyChanged(nameof(HoleRows));
        OnPropertyChanged(nameof(HoleMatrixColumnHeaders));
        OnPropertyChanged(nameof(HoleMatrixRows));
        OnPropertyChanged(nameof(SelectedCellHoleTitle));

        if (rows.Count == 0)
        {
            _selectedHoleNo = 1;
            SelectedHole = null;
            return;
        }

        _selectedHoleNo = Math.Clamp(_selectedHoleNo, 1, rows.Count);
        bool MatchRow64(ST_RECIPE_HOLE_ROW row)
        {
            return row.HoleNo == _selectedHoleNo;
        }

        SelectedHole = rows.FirstOrDefault(MatchRow64) ?? rows[0];
    }

    private static string ToColumnLetter(int oneBasedColumn)
    {
        var value = Math.Max(1, oneBasedColumn);
        var text = "";
        while (value > 0)
        {
            value--;
            text = (char)('A' + (value % 26)) + text;
            value /= 26;
        }

        return text;
    }

    private void SetHoleOverrideValue(
        int cellNo,
        int holeNo,
        string holeName,
        string parameterName,
        string value)
    {
        var normalizedParameter = parameterName.Trim().ToUpperInvariant();
        if (normalizedParameter is not ("OFFSET_X" or "OFFSET_Y"))
        {
            return;
        }

        var axis = normalizedParameter.EndsWith("_Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "X";
        var key = $"CELL{cellNo}_{holeName}_RECIPE_OFFSET_{axis}";
        bool MatchCandidate65(ST_RECIPE_MANAGED_ITEM candidate)
        {
            return candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var item = AllManagedItems.FirstOrDefault(MatchCandidate65);
        if (item is null)
        {
            var displayName = $"Hole {holeName} Recipe Offset {axis}";
            item = new ST_RECIPE_MANAGED_ITEM(
                "CELL",
                "HOLE",
                displayName,
                "0",
                "mm",
                "Per-hole position correction (stored only; preview position is unchanged)",
                "Normal",
                key,
                "HOLE",
                EN_RECIPE_DATA_TYPE.Double,
                0.0,
                -100000.0,
                100000.0);
            AllManagedItems = AllManagedItems.Append(item).ToArray();
            TrackPreviewItems(AllManagedItems);
            OnPropertyChanged(nameof(AllManagedItems));
        }

        item.Value = value;
    }

    private static bool IsHoleOverrideKey(string key)
    {
        if (TryGetCellNo(key) <= 0)
        {
            return false;
        }

        return key.EndsWith("_RECIPE_OFFSET_X", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("_RECIPE_OFFSET_Y", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("_REVIEW_OFFSET_X", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("_REVIEW_OFFSET_Y", StringComparison.OrdinalIgnoreCase);
    }

    private static double ReadManagedDouble(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        string key,
        double defaultValue)
    {
        bool MatchItem66(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var value = managedItems.FirstOrDefault(MatchItem66)?.Value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static double? ReadManagedNullableDouble(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        string key)
    {
        bool MatchItem67(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var value = managedItems.FirstOrDefault(MatchItem67)?.Value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int ReadManagedInt(
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems,
        string key,
        int defaultValue)
    {
        bool MatchItem68(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var value = managedItems.FirstOrDefault(MatchItem68)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static double ReadSettingDouble(
        IReadOnlyList<ST_SYSTEM_PARAMETER> settings,
        string key,
        double defaultValue)
    {
        bool MatchItem69(ST_SYSTEM_PARAMETER item)
        {
            return item.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                        item.Name.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var value = settings.FirstOrDefault(MatchItem69)?.Value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static string FormatPreviewDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<ST_RECIPE_CATEGORY_TAB> BuildCategoryTabs(
        IReadOnlyList<string> categories,
        string selectedCategory)
    {
        ST_RECIPE_CATEGORY_TAB SelectCategory70(string category)
        {
            return new ST_RECIPE_CATEGORY_TAB(
                            category,
                            category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        return categories
            .Select(SelectCategory70)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildGroups(IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems)
    {
        string SelectItem71(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.SourceGroup;
        }

        bool FilterGroup72(string group)
        {
            return !string.IsNullOrWhiteSpace(group);
        }

        var groups = managedItems
            .Select(SelectItem71)
            .Where(FilterGroup72)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new[] { "ALL" }.Concat(groups).ToArray();
    }

    private static IReadOnlyList<ST_RECIPE_GROUP_TAB> BuildGroupTabs(
        IReadOnlyList<string> groups,
        string selectedGroup)
    {
        ST_RECIPE_GROUP_TAB SelectGroup73(string group)
        {
            return new ST_RECIPE_GROUP_TAB(
                            group,
                            group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase));
        }

        return groups
            .Select(SelectGroup73)
            .ToArray();
    }

    private static IReadOnlyList<ST_RECIPE_HISTORY_ROW> BuildChangeHistory(ST_RECIPE_DATA? recipe)
    {
        ST_RECIPE_HISTORY_ROW SelectItem74(ST_RECIPE_HISTORY item)
        {
            return new ST_RECIPE_HISTORY_ROW(
                        item.ChangedAt.ToString("HH:mm:ss"),
                        string.IsNullOrWhiteSpace(item.Action) ? item.OperatorId : item.Action,
                        item.Tab,
                        item.Group,
                        item.ItemName,
                        item.OldValue,
                        item.NewValue);
        }

        return recipe?.History.Select(SelectItem74).ToArray() ?? [];
    }

    private static IReadOnlyList<ST_RECIPE_STATE_ROW> BuildStateRows(
        ST_RECIPE_DATA? recipe,
        string selectedRecipeFile,
        IReadOnlyList<ST_RECIPE_MANAGED_ITEM> managedItems)
    {
        if (recipe is null)
        {
            return
            [
                new("Modified Items", "0"),
                new("Recipe File", "-"),
                new("Edit State", "No Recipe")
            ];
        }
        bool HandleModifiedCount75(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.IsEdited;
        }

        var modifiedCount = managedItems.Count(HandleModifiedCount75);

        return
        [
            new("Modified Items", modifiedCount.ToString(), modifiedCount > 0 ? "Warn" : "Ok"),
            new("Recipe File", selectedRecipeFile, "Accent"),
            new("Edit State", modifiedCount > 0 ? "Modified" : "Loaded", modifiedCount > 0 ? "Warn" : "Ok")
        ];
    }

    private static string NormalizeCategory(string category, IReadOnlyList<string> categories)
    {
        if (categories.Count == 0)
        {
            return "";
        }

        var normalized = NormalizeRecipeText(category, categories[0]);
        bool CheckItem76(string item)
        {
            return item.Equals(normalized, StringComparison.OrdinalIgnoreCase);
        }

        return categories.Any(CheckItem76)
            ? normalized
            : categories[0];
    }

    private static string NormalizeGroup(string group, IReadOnlyList<string> groups)
    {
        var normalized = NormalizeRecipeText(group, "ALL");
        bool CheckItem77(string item)
        {
            return item.Equals(normalized, StringComparison.OrdinalIgnoreCase);
        }

        return groups.Any(CheckItem77)
            ? normalized
            : "ALL";
    }

    private static string NormalizeRecipeText(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeUnit(string unit)
    {
        return string.IsNullOrWhiteSpace(unit) ? "-" : unit;
    }

    private static string GetValueState(ST_RECIPE_PARAM parameter)
    {
        if (IsModified(parameter))
        {
            return "Warn";
        }

        return parameter.Key.Equals("RECIPE_NAME", StringComparison.OrdinalIgnoreCase) ||
            IsOnOffValue(parameter.Value)
                ? "Accent"
                : "Normal";
    }

    private static bool IsModified(ST_RECIPE_PARAM parameter)
    {
        return !string.Equals(
            NormalizeRecipeValue(parameter.Value),
            NormalizeRecipeValue(parameter.DefaultValue),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOnOffValue(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "ON" or "OFF";
    }

    private static string NormalizeRecipeValue(string value)
    {
        return value.Trim();
    }

    private static string GetRecipeIdFromParameter(object? parameter)
    {
        string EvaluateParameterSwitch2()
        {
            var switchValue = parameter;
            switch (switchValue)
            {
                case ST_RECIPE_FILE recipeFile:
                    return recipeFile.FileName;
                case string text:
                    return text;
                default:
                    return "";
            }
        }

        var value = EvaluateParameterSwitch2();

        return Path.GetFileNameWithoutExtension(value.Trim());
    }

    private static ST_RECIPE_PARAM CreateRecipeParameterFromRow(
        ST_RECIPE_MANAGED_ITEM item,
        string recipeId)
    {
        var value = item.Key.Equals("RECIPE_NAME", StringComparison.OrdinalIgnoreCase)
            ? recipeId
            : item.Value;

        return new ST_RECIPE_PARAM(
            item.Item,
            value,
            NormalizeRecipeUnit(item.Unit),
            "",
            item.OriginalValue,
            item.Category,
            item.SourceGroup,
            item.Key,
            item.Description,
            true,
            true,
            0,
            item.DataType,
            item.ChangeLimit,
            item.Min,
            item.Max);
    }

    private IReadOnlyList<ST_RECIPE_PARAM> BuildRecipeParameters(string recipeId)
    {
        ST_RECIPE_PARAM SelectItem78(ST_RECIPE_MANAGED_ITEM item)
        {
            return CreateRecipeParameterFromRow(item, recipeId);
        }

        return AllManagedItems
            .Select(SelectItem78)
            .ToArray();
    }

    private static string? ShowRecipeNameDialog(
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
            ? NormalizeRecipeIdInput(dialog.RecipeName)
            : null;
    }

    private static bool ConfirmRecipeDelete(string recipeId)
    {
        var dialog = new CRecipeConfirmDialog(
            "Delete Recipe",
            $"Delete {recipeId}.csv?\nThis operation removes the recipe file from the RECIPE folder.",
            "DELETE")
        {
            Owner = GetActiveWindow()
        };

        return dialog.ShowDialog() == true;
    }

    private static Window? GetActiveWindow()
    {
        bool MatchWindow79(Window window)
        {
            return window.IsActive;
        }

        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(MatchWindow79);
    }

    private static string NormalizeRecipeIdInput(string value)
    {
        var normalized = value.Trim();

        if (normalized.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.Trim();
    }

    private static string ValidateRecipeId(
        string recipeId,
        IReadOnlyList<ST_RECIPE_DATA> recipes,
        string currentRecipeId = "")
    {
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            return "Recipe name is required.";
        }

        foreach (var character in recipeId)
        {
            if (Path.GetInvalidFileNameChars().Contains(character))
            {
                return $"Recipe name cannot contain '{character}'.";
            }
        }

        if (recipeId is "." or ".." || recipeId.EndsWith(".", StringComparison.Ordinal))
        {
            return "Recipe name is not valid as a file name.";
        }

        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        if (reservedNames.Contains(recipeId))
        {
            return "Recipe name is reserved by Windows.";
        }
        bool CheckRecipe80(ST_RECIPE_DATA recipe)
        {
            return recipe.Id.Equals(recipeId, StringComparison.OrdinalIgnoreCase) &&
                        !recipe.Id.Equals(currentRecipeId, StringComparison.OrdinalIgnoreCase);
        }

        var exists = recipes.Any(CheckRecipe80);

        return exists
            ? $"Recipe {recipeId}.csv already exists."
            : "";
    }

    private static string ValidateRecipeParameters(IReadOnlyList<ST_RECIPE_PARAM> parameters)
    {
        foreach (var parameter in parameters)
        {
            var value = parameter.Value.Trim();

            if (parameter.Key.Equals("RECIPE_NAME", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(value))
            {
                return "Recipe save blocked. Recipe Name cannot be empty.";
            }
            string EvaluateDataTypeSwitch3()
            {
                var switchValue = parameter.DataType;
                switch (switchValue)
                {
                    case EN_RECIPE_DATA_TYPE.Int:
                        return ValidateIntParameter(parameter, value);
                    case EN_RECIPE_DATA_TYPE.Double:
                        return ValidateDoubleParameter(parameter, value);
                    case EN_RECIPE_DATA_TYPE.Bool:
                        return ValidateBoolParameter(parameter, value);
                    default:
                        return "";
                }
            }

            var validationMessage = EvaluateDataTypeSwitch3();

            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                return validationMessage;
            }
        }

        return "";
    }

    private static string ValidateIntParameter(ST_RECIPE_PARAM parameter, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"Recipe save blocked. {parameter.Name} must be an integer.";
        }

        return ValidateNumericRange(parameter, parsed);
    }

    private static string ValidateDoubleParameter(ST_RECIPE_PARAM parameter, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"Recipe save blocked. {parameter.Name} must be numeric.";
        }

        return ValidateNumericRange(parameter, parsed);
    }

    private static string ValidateBoolParameter(ST_RECIPE_PARAM parameter, string value)
    {
        var normalized = value.Trim().ToUpperInvariant();

        return normalized is "ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0" or "YES" or "NO"
            ? ""
            : $"Recipe save blocked. {parameter.Name} must be ON/OFF or TRUE/FALSE.";
    }

    private static string ValidateNumericRange(ST_RECIPE_PARAM parameter, double value)
    {
        if (!parameter.Min.Equals(parameter.Max) &&
            (value < parameter.Min || value > parameter.Max))
        {
            return $"Recipe save blocked. {parameter.Name} must be between {parameter.Min:0.###} and {parameter.Max:0.###}.";
        }

        if (parameter.ChangeLimit > 0 &&
            double.TryParse(parameter.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var oldValue) &&
            Math.Abs(value - oldValue) > parameter.ChangeLimit)
        {
            return $"Recipe save blocked. {parameter.Name} change limit is +/-{parameter.ChangeLimit:0.###}.";
        }

        return "";
    }

    private static string GetEditedRecipeName(
        IReadOnlyList<ST_RECIPE_PARAM> parameters,
        string fallbackRecipeId)
    {
        bool MatchItem81(ST_RECIPE_PARAM item)
        {
            return item.Key.Equals("RECIPE_NAME", StringComparison.OrdinalIgnoreCase) ||
                            item.Name.Equals("Recipe Name", StringComparison.OrdinalIgnoreCase);
        }

        return parameters.FirstOrDefault(MatchItem81)?.Value
            ?? fallbackRecipeId;
    }

    private static string NormalizeRecipeUnit(string unit)
    {
        return unit == "-" ? "" : unit;
    }
}

public sealed record ST_RECIPE_CATEGORY_TAB(
    string Category,
    bool IsSelected);

public sealed record ST_RECIPE_GROUP_TAB(
    string Group,
    bool IsSelected);

public sealed record ST_RECIPE_CELL(
    int CellNo,
    IReadOnlyList<ST_RECIPE_MANAGED_ITEM> Items,
    IReadOnlyList<ST_CELL_DRILL_POINT> Points);

public sealed record ST_RECIPE_DISTORTION_KEY_ITEM(
    int KeyNo,
    ST_RECIPE_MANAGED_ITEM? XItem,
    ST_RECIPE_MANAGED_ITEM? YItem)
{
    public string DisplayText
    {
        get
        {
            return $"DK{KeyNo}";
        }
    }

    public string XLabel
    {
        get
        {
            return $"AK1 To DK{KeyNo} X";
        }
    }

    public string YLabel
    {
        get
        {
            return $"AK1 To DK{KeyNo} Y";
        }
    }
}

public sealed class ST_RECIPE_CELL_OVERVIEW_ROW : CBindingBase
{
    private readonly IReadOnlyDictionary<string, ST_RECIPE_MANAGED_ITEM> _items;
    private readonly Action<int, bool> _selectionChanged;
    private bool _isSelected;

    public ST_RECIPE_CELL_OVERVIEW_ROW(
        ST_RECIPE_CELL cell,
        bool isCurrent,
        bool isSelected,
        Action<int, bool> selectionChanged)
    {
        CellNo = cell.CellNo;
        IsCurrent = isCurrent;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
        var keyPrefix = $"CELL{CellNo}_";
        bool FilterItem82(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase);
        }

        string HandleItems83(ST_RECIPE_MANAGED_ITEM item)
        {
            return item.Key[keyPrefix.Length..];
        }

        ST_RECIPE_MANAGED_ITEM HandleItems84(ST_RECIPE_MANAGED_ITEM item)
        {
            return item;
        }

        _items = cell.Items
            .Where(FilterItem82)
            .ToDictionary(
HandleItems83,
HandleItems84,
                StringComparer.OrdinalIgnoreCase);
    }

    public int CellNo { get; }

    public bool IsCurrent { get; }

    public bool IsSelected
    {
        get
        {
            return _isSelected;
        }

        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged(CellNo, value);
            }
        }
    }

    public string FirstX {
        get
        {
            return Get("ALIGN_TO_1ST_PIXEL_X");
        }

        set
        {
            Set("ALIGN_TO_1ST_PIXEL_X", value);
        }
    }
    public string FirstY {
        get
        {
            return Get("ALIGN_TO_1ST_PIXEL_Y");
        }

        set
        {
            Set("ALIGN_TO_1ST_PIXEL_Y", value);
        }
    }
    public string Rotation {
        get
        {
            return Get("ROTATION");
        }

        set
        {
            Set("ROTATION", value);
        }
    }
    public string CountX {
        get
        {
            return Get("NUM_OF_PIXEL_X");
        }

        set
        {
            Set("NUM_OF_PIXEL_X", value);
        }
    }
    public string CountY {
        get
        {
            return Get("NUM_OF_PIXEL_Y");
        }

        set
        {
            Set("NUM_OF_PIXEL_Y", value);
        }
    }
    public string PitchX {
        get
        {
            return Get("PITCH_X");
        }

        set
        {
            Set("PITCH_X", value);
        }
    }
    public string PitchY {
        get
        {
            return Get("PITCH_Y");
        }

        set
        {
            Set("PITCH_Y", value);
        }
    }
    public string PixelSize {
        get
        {
            return Get("PIXEL_SIZE");
        }

        set
        {
            Set("PIXEL_SIZE", value);
        }
    }

    private string Get(string parameterName)
    {
        return _items.TryGetValue(parameterName, out var item) ? item.Value : "";
    }

    private void Set(string parameterName, string value)
    {
        if (!_items.TryGetValue(parameterName, out var item) || item.Value == value)
        {
            return;
        }

        item.Value = value;
        string EvaluateParameterNameSwitch4()
        {
            var switchValue = parameterName;
            switch (switchValue)
            {
                case "ALIGN_TO_1ST_PIXEL_X":
                    return nameof(FirstX);
                case "ALIGN_TO_1ST_PIXEL_Y":
                    return nameof(FirstY);
                case "ROTATION":
                    return nameof(Rotation);
                case "NUM_OF_PIXEL_X":
                    return nameof(CountX);
                case "NUM_OF_PIXEL_Y":
                    return nameof(CountY);
                case "PITCH_X":
                    return nameof(PitchX);
                case "PITCH_Y":
                    return nameof(PitchY);
                case "PIXEL_SIZE":
                    return nameof(PixelSize);
                default:
                    return parameterName;
            }
        }

        OnPropertyChanged(EvaluateParameterNameSwitch4());
    }
}

public sealed class ST_RECIPE_HOLE_ROW : CBindingBase
{
    private readonly Action<string, string> _overrideChanged;
    private string _offsetX;
    private string _offsetY;
    private bool _isSelected;

    public ST_RECIPE_HOLE_ROW(
        int holeNo,
        int row,
        int column,
        bool isInsideGlass,
        string offsetX,
        string offsetY,
        Action<string, string> overrideChanged)
    {
        HoleNo = holeNo;
        Row = row;
        Column = column;
        IsInsideGlass = isInsideGlass;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _overrideChanged = overrideChanged;
    }

    public int HoleNo { get; }

    public int Row { get; }

    public int Column { get; }

    public string MatrixPointName
    {
        get
        {
            return $"{ToColumnLetter(Column)}{Row}";
        }
    }

    public string OffsetCoordinateText
    {
        get
        {
            return $"({NormalizeOffsetText(OffsetX)}, {NormalizeOffsetText(OffsetY)})";
        }
    }

    public bool IsInsideGlass { get; }

    public string PlacementState
    {
        get
        {
            return IsInsideGlass ? "IN GLASS" : "OUTSIDE";
        }
    }

    public bool IsSelected
    {
        get
        {
            return _isSelected;
        }

        set
        {
            SetProperty(ref _isSelected, value);
        }
    }

    public string OffsetX
    {
        get
        {
            return _offsetX;
        }

        set
        {
            SetOverride(ref _offsetX, value, "OFFSET_X");
        }
    }

    public string OffsetY
    {
        get
        {
            return _offsetY;
        }

        set
        {
            SetOverride(ref _offsetY, value, "OFFSET_Y");
        }
    }

    private void SetOverride(ref string field, string value, string parameterName)
    {
        if (SetProperty(ref field, value))
        {
            OnPropertyChanged(parameterName == "OFFSET_X" ? nameof(OffsetX) : nameof(OffsetY));
            OnPropertyChanged(nameof(OffsetCoordinateText));
            _overrideChanged(parameterName, value);
        }
    }

    private static string NormalizeOffsetText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    }

    private static string ToColumnLetter(int oneBasedColumn)
    {
        var value = Math.Max(1, oneBasedColumn);
        var text = "";
        while (value > 0)
        {
            value--;
            text = (char)('A' + (value % 26)) + text;
            value /= 26;
        }

        return text;
    }
}

public sealed record ST_RECIPE_HOLE_MATRIX_ROW(
    int RowNo,
    IReadOnlyList<ST_RECIPE_HOLE_ROW> Cells);

public sealed record ST_RECIPE_LAYOUT_PREVIEW(
    ImageSource? CellImage,
    IReadOnlyList<ST_CELL_PREVIEW_LABEL> CellLabels);

public sealed record ST_RECIPE_HEAD_PREVIEW(
    ImageSource? Image,
    IReadOnlyList<ST_HEAD_COVERAGE_PREVIEW_LABEL> Labels,
    string SummaryText);

public sealed record ST_HEAD_COVERAGE_PREVIEW_LABEL(
    int HeadNo,
    double CanvasCenterX,
    double CanvasCenterY,
    double Width,
    double DesignWidth,
    double DesignHeight,
    bool IsSelected)
{
    public string DisplayText
    {
        get
        {
            return $"H{HeadNo:00}";
        }
    }

    public double Height
    {
        get
        {
            return 24.0;
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            return IsSelected
        ? CMenuMain.CreateHeadBrush(HeadNo, 230)
        : new SolidColorBrush(Color.FromRgb(31, 41, 55));
        }
    }

    public Brush BorderBrush
    {
        get
        {
            return IsSelected
        ? CMenuMain.CreateHeadBrush(HeadNo, 255)
        : new SolidColorBrush(Color.FromRgb(102, 136, 164));
        }
    }

    public Brush TextBrush
    {
        get
        {
            return IsSelected
        ? Brushes.White
        : new SolidColorBrush(Color.FromRgb(226, 232, 240));
        }
    }
}

public sealed record ST_RECIPE_FILE(
    string No,
    string FileName,
    bool IsSelected);

public sealed class ST_RECIPE_MANAGED_ITEM : CBindingBase
{
    private readonly string _initialValue;
    private readonly string _initialValueState;
    private string _value;
    private string _valueState;

    public ST_RECIPE_MANAGED_ITEM(
        string category,
        string group,
        string item,
        string value,
        string unit,
        string description,
        string valueState = "Normal",
        string key = "",
        string sourceGroup = "",
        EN_RECIPE_DATA_TYPE dataType = EN_RECIPE_DATA_TYPE.String,
        double changeLimit = 0.0,
        double min = 0.0,
        double max = 0.0)
    {
        Category = category;
        Group = group;
        Item = item;
        Unit = unit;
        Description = description;
        Key = key;
        SourceGroup = sourceGroup;
        DataType = dataType;
        ChangeLimit = changeLimit;
        Min = min;
        Max = max;
        _value = value;
        _valueState = valueState;
        _initialValue = value;
        _initialValueState = valueState;
    }

    public string Category { get; }

    public string Group { get; }

    public string Item { get; }

    public string Value
    {
        get
        {
            return _value;
        }

        set
        {
            if (!SetProperty(ref _value, value))
            {
                return;
            }

            ValueState = IsEdited ? "Warn" : _initialValueState;
            OnPropertyChanged(nameof(IsEdited));
        }
    }

    public string Unit { get; }

    public string Description { get; }

    public string Key { get; }

    public string SourceGroup { get; }

    public string OriginalValue
    {
        get
        {
            return _initialValue;
        }
    }

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

    public double ChangeLimit { get; }

    public double Min { get; }

    public double Max { get; }

    public bool IsEdited
    {
        get
        {
            return !NormalizeValue(Value).Equals(NormalizeValue(_initialValue), StringComparison.OrdinalIgnoreCase);
        }
    }

    public string ValueState
    {
        get
        {
            return _valueState;
        }

        private set
        {
            if (SetProperty(ref _valueState, value))
            {
                OnPropertyChanged(nameof(ValueBrush));
            }
        }
    }

    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateValueStateSwitch5()
            {
                var switchValue = ValueState;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    case "Warn":
                        return CStatusBrush.Wait;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateValueStateSwitch5();
        }
    }

    private static string NormalizeValue(string value)
    {
        return value.Trim();
    }
}

public sealed record ST_RECIPE_HISTORY_ROW(
    string Time,
    string Action,
    string Tab,
    string Group,
    string Item,
    string Before,
    string After)
{
    public Brush AfterBrush
    {
        get
        {
            return CStatusBrush.Wait;
        }
    }
}

public sealed record ST_RECIPE_STATE_ROW(
    string Name,
    string Value,
    string State = "Normal")
{
    public Brush ValueBrush
    {
        get
        {
            Brush EvaluateStateSwitch6()
            {
                var switchValue = State;
                switch (switchValue)
                {
                    case "Accent":
                        return CStatusBrush.Simul;
                    case "Warn":
                        return CStatusBrush.Wait;
                    case "Ok":
                        return CStatusBrush.Online;
                    default:
                        return CStatusBrush.PrimaryText;
                }
            }

            return EvaluateStateSwitch6();
        }
    }
}




