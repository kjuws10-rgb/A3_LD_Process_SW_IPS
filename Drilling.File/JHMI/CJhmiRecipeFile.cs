using Drilling.Common.Log;
using System.Globalization;
using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;
using Drilling.File.Parser;

namespace Drilling.File.JHMI;

public sealed class CJhmiRecipeFile(string configRoot) : IRecipeFile
{
    private static readonly HashSet<string> CellScopedTemplateNames = new(StringComparer.OrdinalIgnoreCase)
    {
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
    };

    private static readonly IReadOnlyList<string> FormHeaders =
    [
        "TAB",
        "GROUP",
        "NAME",
        "DISPLAY NAME",
        "CIM NAME",
        "DATA TYPE",
        "UNIT",
        "SHOW",
        "USE",
        "VALUE",
        "SCALE",
        "CHANGE LIMIT",
        "MIN",
        "MAX",
        "DESCRIPTION",
        "ORDER"
    ];

    private readonly string _configRoot = configRoot;
    private readonly string _recipeDirectory = Path.Combine(configRoot, "RECIPE");
    private readonly CLogManager _logManager = new(configRoot);

    public Task<IReadOnlyList<ST_RECIPE_DATA>> LoadAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_recipeDirectory))
        {
            return Task.FromResult<IReadOnlyList<ST_RECIPE_DATA>>([]);
        }

        var formItems = LoadFormItems();
        var recipes = Directory.EnumerateFiles(_recipeDirectory, "*.csv")
            .Select(path => LoadRecipe(path, formItems))
            .Where(recipe => recipe is not null)
            .Cast<ST_RECIPE_DATA>()
            .OrderBy(recipe => recipe.Name)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ST_RECIPE_DATA>>(recipes);
    }

    public Task<ST_RECIPE_DATA?> Find(string recipeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var formItems = LoadFormItems();
        var path = GetRecipePath(recipeId);
        var recipe = System.IO.File.Exists(path)
            ? LoadRecipe(path, formItems)
            : null;

        return Task.FromResult(recipe);
    }

    public Task Save(ST_RECIPE_DATA recipe, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_recipeDirectory);

        var formItems = LoadFormItems();
        var recipePath = GetRecipePath(recipe.Id);
        var recipeExists = System.IO.File.Exists(recipePath);
        var oldValues = recipeExists
            ? ReadRecipeValues(recipePath)
                .GroupBy(item => CreateKey(item.Tab, item.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var valuesByKey = recipe.Parameters
            .Where(parameter => !IsCellScopedKey(parameter.Key))
            .Select(parameter => new
            {
                Parameter = parameter,
                FormItem = FindFormItem(formItems, parameter)
            })
            .Where(item => item.FormItem is not null)
            .ToDictionary(
                item => CreateKey(item.FormItem!.Tab, item.FormItem.Name),
                item => item.Parameter.Value,
                StringComparer.OrdinalIgnoreCase);
        var activeCellCount = GetActiveCellCount(recipe.Parameters);
        var cellScopedParameters = recipe.Parameters
            .Where(parameter => IsCellScopedKey(parameter.Key) &&
                GetCellNo(parameter.Key) <= activeCellCount &&
                IsHoleOverrideWithinActivePattern(parameter, recipe.Parameters))
            .OrderBy(parameter => GetCellNo(parameter.Key))
            .ThenBy(parameter => parameter.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lines = new List<string>
        {
            FormatRecipeLine("DEFAULT", "RECIPE_NAME", recipe.Name),
            FormatRecipeLine("DEFAULT", "RECIPE_ID", recipe.Id)
        };
        var expectedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CreateKey("DEFAULT", "RECIPE_NAME")] = recipe.Name,
            [CreateKey("DEFAULT", "RECIPE_ID")] = recipe.Id
        };

        var changedItems = new List<(string Tab, string Group, string ItemName, string OldValue, string NewValue)>();

        foreach (var item in formItems
            .Where(item => item.Use && !IsCellScopedTemplate(item))
            .OrderBy(item => item.DisplayOrder))
        {
            var key = CreateKey(item.Tab, item.Name);
            var value = valuesByKey.TryGetValue(key, out var editedValue)
                ? editedValue
                : item.DefaultValue;

            if (recipeExists)
            {
                var oldValue = GetValue(oldValues, item.Tab, item.Name, item.DefaultValue);

                if (!oldValue.Equals(value, StringComparison.Ordinal))
                {
                    changedItems.Add((item.Tab, item.Group, item.DisplayName, oldValue, value));
                }
            }

            expectedValues[CreateKey(item.Tab, item.Name)] = value;
            lines.Add(FormatRecipeLine(item.Tab, item.Name, value));
        }

        foreach (var parameter in cellScopedParameters)
        {
            var persistedKey = ToPersistedCellScopedKey(parameter.Key);
            if (recipeExists)
            {
                var oldValue = GetValue(oldValues, "CELL", persistedKey, parameter.DefaultValue);

                if (!oldValue.Equals(parameter.Value, StringComparison.Ordinal))
                {
                    changedItems.Add((
                        "CELL",
                        parameter.Group,
                        GetCellHistoryItemName(parameter, recipe.Parameters),
                        oldValue,
                        parameter.Value));
                }
            }

            expectedValues[CreateKey("CELL", persistedKey)] = parameter.Value;
            lines.Add(FormatRecipeLine("CELL", persistedKey, parameter.Value));
        }

        System.IO.File.WriteAllLines(recipePath, lines);
        ValidateSavedRecipeFile(recipePath, expectedValues);

        var recipeName = GetRecipeName(recipe);

        if (recipeExists)
        {
            foreach (var changedItem in changedItems)
            {
                _logManager.WriteRecipeModify(
                    recipeName,
                    changedItem.Tab,
                    changedItem.Group,
                    changedItem.ItemName,
                    changedItem.OldValue,
                    changedItem.NewValue);
            }

            _logManager.WriteRecipeSave(recipeName);
        }
        else
        {
            _logManager.WriteRecipeCreate(recipeName);
        }

        return Task.CompletedTask;
    }

    public Task Rename(
        string oldRecipeId,
        string newRecipeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_recipeDirectory);

        var oldPath = GetRecipePath(oldRecipeId);
        var newPath = GetRecipePath(newRecipeId);

        if (!System.IO.File.Exists(oldPath))
        {
            return Task.CompletedTask;
        }

        if (System.IO.File.Exists(newPath))
        {
            throw new IOException($"Recipe file already exists. {newRecipeId}.csv");
        }

        var formItems = LoadFormItems();
        var oldValues = ReadRecipeValues(oldPath)
            .GroupBy(item => CreateKey(item.Tab, item.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);
        var oldRecipeName = GetValue(oldValues, "DEFAULT", "RECIPE_NAME", oldRecipeId);
        var activeCellCount = GetActiveCellCount(oldValues);
        var expectedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CreateKey("DEFAULT", "RECIPE_NAME")] = newRecipeId,
            [CreateKey("DEFAULT", "RECIPE_ID")] = newRecipeId
        };
        var lines = new List<string>
        {
            FormatRecipeLine("DEFAULT", "RECIPE_NAME", newRecipeId),
            FormatRecipeLine("DEFAULT", "RECIPE_ID", newRecipeId)
        };

        foreach (var item in formItems
            .Where(item => item.Use && !IsCellScopedTemplate(item))
            .OrderBy(item => item.DisplayOrder))
        {
            var value = item.Name.Equals("RECIPE_NAME", StringComparison.OrdinalIgnoreCase)
                ? newRecipeId
                : GetValue(oldValues, item.Tab, item.Name, item.DefaultValue);

            expectedValues[CreateKey(item.Tab, item.Name)] = value;
            lines.Add(FormatRecipeLine(item.Tab, item.Name, value));
        }

        foreach (var cellValue in ReadRecipeValues(oldPath)
            .Where(item => item.Tab.Equals("CELL", StringComparison.OrdinalIgnoreCase) &&
                IsCellScopedKey(item.Name) &&
                GetCellNo(item.Name) <= activeCellCount)
            .OrderBy(item => GetCellNo(item.Name))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parameterName = NormalizeCellScopedParameterName(GetCellParameterName(cellValue.Name));
            var scopedKey = $"CELL{GetCellNo(cellValue.Name)}_{parameterName}";
            var persistedKey = ToPersistedCellScopedKey(scopedKey);
            expectedValues[CreateKey("CELL", persistedKey)] = cellValue.Value;
            lines.Add(FormatRecipeLine("CELL", persistedKey, cellValue.Value));
        }

        System.IO.File.WriteAllLines(newPath, lines);
        ValidateSavedRecipeFile(newPath, expectedValues);
        System.IO.File.Delete(oldPath);
        _logManager.WriteRecipeRename(oldRecipeName, newRecipeId);

        return Task.CompletedTask;
    }

    public Task Delete(string recipeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetRecipePath(recipeId);
        var recipeName = recipeId;
        var recipeExists = System.IO.File.Exists(path);

        if (recipeExists)
        {
            var values = ReadRecipeValues(path)
                .GroupBy(item => CreateKey(item.Tab, item.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value,
                    StringComparer.OrdinalIgnoreCase);

            recipeName = GetValue(values, "DEFAULT", "RECIPE_NAME", recipeId);
        }

        DeleteIfExists(path);

        if (recipeExists)
        {
            _logManager.WriteRecipeDelete(recipeName);
        }

        return Task.CompletedTask;
    }

    private ST_RECIPE_DATA? LoadRecipe(string path, IReadOnlyList<ST_RECIPE_FORM_ITEM> formItems)
    {
        var recipeId = Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrWhiteSpace(recipeId))
        {
            return null;
        }

        var values = ReadRecipeValues(path)
            .GroupBy(item => CreateKey(item.Tab, item.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

        var recipeName = GetValue(values, "DEFAULT", "RECIPE_NAME", recipeId);
        var parameters = formItems
            .Where(item => item.Use)
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new ST_RECIPE_PARAM(
                item.DisplayName,
                GetValue(values, item.Tab, item.Name, item.DefaultValue),
                item.Unit,
                $"{item.Min.ToString("0.###", CultureInfo.InvariantCulture)} - {item.Max.ToString("0.###", CultureInfo.InvariantCulture)}",
                item.DefaultValue,
                item.Tab,
                item.Group,
                item.Name,
                item.Description,
                item.Show,
                item.Use,
                item.DisplayOrder,
                item.DataType,
                item.ChangeLimit,
                item.Min,
                item.Max,
                item.Extra))
            .ToList();

        var formItemsByName = formItems
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var cellValue in ReadRecipeValues(path)
            .Where(item => item.Tab.Equals("CELL", StringComparison.OrdinalIgnoreCase) && IsCellScopedKey(item.Name))
            .OrderBy(item => GetCellNo(item.Name))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parameterName = NormalizeCellScopedParameterName(GetCellParameterName(cellValue.Name));
            var hasTemplate = formItemsByName.TryGetValue(parameterName, out var template) ||
                formItemsByName.TryGetValue($"CELL_{parameterName}", out template);
            var normalizedScopedKey = $"CELL{GetCellNo(cellValue.Name)}_{parameterName}";
            if (parameters.Any(parameter => parameter.Key.Equals(normalizedScopedKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!hasTemplate)
            {
                var holeOverride = CreateHoleOverrideParameter(
                    GetCellNo(cellValue.Name),
                    parameterName,
                    normalizedScopedKey,
                    cellValue.Value);
                if (holeOverride is not null)
                {
                    parameters.Add(holeOverride);
                }
                continue;
            }

            parameters.Add(new ST_RECIPE_PARAM(
                template!.DisplayName,
                cellValue.Value,
                template.Unit,
                $"{template.Min.ToString("0.###", CultureInfo.InvariantCulture)} - {template.Max.ToString("0.###", CultureInfo.InvariantCulture)}",
                template.DefaultValue,
                "CELL",
                GetCellScopedGroup(parameterName),
                normalizedScopedKey,
                template.Description,
                true,
                true,
                template.DisplayOrder,
                template.DataType,
                template.ChangeLimit,
                template.Min,
                template.Max,
                template.Extra));
        }

        var history = _logManager.ReadRecipeRecent(recipeName, recipeId);

        return new ST_RECIPE_DATA(recipeId, recipeName, parameters.ToArray(), history);
    }

    private static bool IsCellScopedKey(string key)
    {
        return GetCellNo(key) > 0 && !string.IsNullOrWhiteSpace(GetCellParameterName(key));
    }

    private static bool IsCellScopedTemplate(ST_RECIPE_FORM_ITEM item)
    {
        return item.Tab.Equals("PROCESS", StringComparison.OrdinalIgnoreCase) &&
            CellScopedTemplateNames.Contains(item.Name);
    }

    private static int GetActiveCellCount(IEnumerable<ST_RECIPE_PARAM> parameters)
    {
        var countValue = parameters.FirstOrDefault(parameter =>
            parameter.Key.Equals("CELL_COUNT", StringComparison.OrdinalIgnoreCase))?.Value;

        int.TryParse(countValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);

        return Math.Max(1, count);
    }

    private static bool IsHoleOverrideWithinActivePattern(
        ST_RECIPE_PARAM parameter,
        IReadOnlyList<ST_RECIPE_PARAM> parameters)
    {
        var parameterName = NormalizeCellScopedParameterName(GetCellParameterName(parameter.Key));
        var cellNo = GetCellNo(parameter.Key);
        var countXValue = parameters.FirstOrDefault(item =>
            item.Key.Equals($"CELL{cellNo}_NUM_OF_PIXEL_X", StringComparison.OrdinalIgnoreCase))?.Value;
        var countYValue = parameters.FirstOrDefault(item =>
            item.Key.Equals($"CELL{cellNo}_NUM_OF_PIXEL_Y", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!int.TryParse(countXValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var countX) ||
            !int.TryParse(countYValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var countY) ||
            countX <= 0 ||
            countY <= 0)
        {
            return true;
        }

        return !TryGetNamedHoleOverrideMetadata(
                parameterName,
                out _,
                out var column,
                out var row,
                out _,
                out _,
                out _,
                out _) ||
            column <= countX && row <= countY;
    }

    private static int GetActiveCellCount(IReadOnlyDictionary<string, string> values)
    {
        var countValue = GetValue(values, "PROCESS", "CELL_COUNT", "");
        int.TryParse(countValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);

        return Math.Max(1, count);
    }

    private static int GetCellNo(string key)
    {
        if (!key.StartsWith("CELL", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var separatorIndex = GetCellKeySeparatorIndex(key);
        return separatorIndex > 4 &&
            int.TryParse(key.AsSpan(4, separatorIndex - 4), NumberStyles.None, CultureInfo.InvariantCulture, out var cellNo)
                ? cellNo
                : 0;
    }

    private static string GetCellParameterName(string key)
    {
        var separatorIndex = GetCellKeySeparatorIndex(key);
        return separatorIndex >= 0 && separatorIndex < key.Length - 1
            ? key[(separatorIndex + 1)..]
            : "";
    }

    private static int GetCellKeySeparatorIndex(string key)
    {
        return key.IndexOf('_', 4);
    }

    private static string NormalizeCellScopedParameterName(string parameterName)
    {
        return parameterName.StartsWith("CELL_", StringComparison.OrdinalIgnoreCase)
            ? parameterName[5..]
            : parameterName;
    }

    private static string ToPersistedCellScopedKey(string key)
    {
        return key;
    }

    private static string GetCellScopedGroup(string parameterName)
    {
        if (TryGetNamedHoleOverrideMetadata(
                parameterName,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _))
        {
            return "HOLE";
        }

        return parameterName.Equals("ALIGN_TO_1ST_PIXEL_X", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Equals("ALIGN_TO_1ST_PIXEL_Y", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Equals("ROTATION", StringComparison.OrdinalIgnoreCase)
                ? "CELL"
                : "POINT";
    }

    private static string GetCellHistoryItemName(
        ST_RECIPE_PARAM parameter,
        IReadOnlyList<ST_RECIPE_PARAM> parameters)
    {
        var cellNo = GetCellNo(parameter.Key);
        var parameterName = NormalizeCellScopedParameterName(GetCellParameterName(parameter.Key));

        if (TryGetNamedHoleOverrideMetadata(
                parameterName,
                out var holeName,
                out _,
                out _,
                out var namedDisplayName,
                out _,
                out _,
                out _))
        {
            return $"Cell{cellNo} {holeName} {namedDisplayName}";
        }

        var itemName = parameterName.ToUpperInvariant() switch
        {
            "PIXEL_SIZE" => "Hole Size",
            "NUM_OF_PIXEL_X" => "Hole Count X",
            "NUM_OF_PIXEL_Y" => "Hole Count Y",
            _ => parameter.Name
        };
        return $"Cell{cellNo} {itemName}";
    }

    private static ST_RECIPE_PARAM? CreateHoleOverrideParameter(
        int cellNo,
        string parameterName,
        string scopedKey,
        string value)
    {
        if (cellNo <= 0)
        {
            return null;
        }

        if (TryGetNamedHoleOverrideMetadata(
                parameterName,
                out var holeName,
                out _,
                out _,
                out var namedDisplayName,
                out var namedUnit,
                out var namedMin,
                out var namedMax))
        {
            return new ST_RECIPE_PARAM(
                $"Hole {holeName} {namedDisplayName}",
                value,
                namedUnit,
                $"{namedMin.ToString("0.###", CultureInfo.InvariantCulture)} - {namedMax.ToString("0.###", CultureInfo.InvariantCulture)}",
                "0",
                "CELL",
                "HOLE",
                scopedKey,
                "Per-hole position correction",
                true,
                true,
                0,
                EN_RECIPE_DATA_TYPE.Double,
                0.0,
                namedMin,
                namedMax);
        }

        return null;
    }

    private static bool TryGetNamedHoleOverrideMetadata(
        string parameterName,
        out string holeName,
        out int column,
        out int row,
        out string displayName,
        out string unit,
        out double min,
        out double max)
    {
        holeName = "";
        column = 0;
        row = 0;
        displayName = "";
        unit = "";
        min = 0.0;
        max = 0.0;

        var normalized = parameterName.Trim().ToUpperInvariant();
        var fields = new[]
        {
            (Suffix: "_RECIPE_OFFSET_X", DisplayName: "Recipe Offset X"),
            (Suffix: "_RECIPE_OFFSET_Y", DisplayName: "Recipe Offset Y"),
            (Suffix: "_REVIEW_OFFSET_X", DisplayName: "Review Offset X"),
            (Suffix: "_REVIEW_OFFSET_Y", DisplayName: "Review Offset Y")
        };
        var field = fields.FirstOrDefault(candidate =>
            normalized.EndsWith(candidate.Suffix, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(field.Suffix))
        {
            return false;
        }

        var matrixName = normalized[..^field.Suffix.Length];
        var rowStart = 0;
        while (rowStart < matrixName.Length && matrixName[rowStart] is >= 'A' and <= 'Z')
        {
            rowStart++;
        }

        if (rowStart == 0 ||
            rowStart == matrixName.Length ||
            !int.TryParse(
                matrixName.AsSpan(rowStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out row) ||
            row <= 0)
        {
            return false;
        }

        foreach (var letter in matrixName.AsSpan(0, rowStart))
        {
            column = checked((column * 26) + (letter - 'A' + 1));
        }

        holeName = matrixName;
        displayName = field.DisplayName;
        unit = "mm";
        min = -100000.0;
        max = 100000.0;
        return column > 0;
    }

    private IReadOnlyList<ST_RECIPE_FORM_ITEM> LoadFormItems()
    {
        CCsvParser.ValidateRequiredHeaders(
            GetFormPath(),
            "JHMI_RCP",
            FormHeaders.Select(header => new[] { header }));

        return CCsvParser.Read(GetFormPath())
            .Select((row, index) => new ST_RECIPE_FORM_ITEM(
                CCsvParser.Get(row, "TAB"),
                CCsvParser.Get(row, "GROUP"),
                CCsvParser.Get(row, "NAME"),
                GetOrDefault(CCsvParser.Get(row, "DISPLAY NAME"), CCsvParser.Get(row, "NAME")),
                GetOrDefault(CCsvParser.Get(row, "CIM NAME"), CCsvParser.Get(row, "NAME")),
                ReadDataType(CCsvParser.Get(row, "DATA TYPE")),
                CCsvParser.Get(row, "UNIT"),
                ReadBool(CCsvParser.Get(row, "SHOW"), true),
                ReadBool(CCsvParser.Get(row, "USE"), true),
                CCsvParser.Get(row, "VALUE"),
                ReadDouble(CCsvParser.Get(row, "SCALE"), 1.0),
                ReadDouble(CCsvParser.Get(row, "CHANGE LIMIT"), 0.0),
                ReadDouble(CCsvParser.Get(row, "MIN"), 0.0),
                ReadDouble(CCsvParser.Get(row, "MAX"), 0.0),
                CCsvParser.Get(row, "DESCRIPTION"),
                ReadInt(CCsvParser.Get(row, "ORDER"), index + 1),
                CCsvParser.GetExtra(row, FormHeaders)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Tab) && !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    private static IReadOnlyList<ST_RECIPE_VALUE> ReadRecipeValues(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return [];
        }

        return System.IO.File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseRecipeLine)
            .Where(item => item is not null)
            .Cast<ST_RECIPE_VALUE>()
            .ToArray();
    }

    private static ST_RECIPE_VALUE? ParseRecipeLine(string line)
    {
        var fields = SplitCsvLine(line);

        if (fields.Count < 3)
        {
            return null;
        }

        return new ST_RECIPE_VALUE(fields[0], fields[1], fields[2], fields.Skip(3).ToArray());
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];

            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (current == ',' && !inQuotes)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(current);
            }
        }

        fields.Add(value.ToString());
        return fields;
    }

    private string GetFormPath()
    {
        return Path.Combine(_configRoot, "JHMI_RCP.csv");
    }

    private string GetRecipePath(string recipeId)
    {
        return Path.Combine(_recipeDirectory, $"{GetSafeFileName(recipeId)}.csv");
    }

    private static ST_RECIPE_FORM_ITEM? FindFormItem(
        IReadOnlyList<ST_RECIPE_FORM_ITEM> formItems,
        ST_RECIPE_PARAM parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Tab) && !string.IsNullOrWhiteSpace(parameter.Key))
        {
            var formItem = formItems.FirstOrDefault(item =>
                item.Tab.Equals(parameter.Tab, StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase));

            if (formItem is not null)
            {
                return formItem;
            }
        }

        if (!string.IsNullOrWhiteSpace(parameter.Key))
        {
            var formItem = formItems.FirstOrDefault(item =>
                item.Name.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase));

            if (formItem is not null)
            {
                return formItem;
            }
        }

        return formItems.FirstOrDefault(item =>
                item.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase))
            ?? formItems.FirstOrDefault(item =>
                item.DisplayName.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static EN_RECIPE_DATA_TYPE ReadDataType(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "INT" => EN_RECIPE_DATA_TYPE.Int,
            "DOUBLE" => EN_RECIPE_DATA_TYPE.Double,
            "BOOL" => EN_RECIPE_DATA_TYPE.Bool,
            _ => EN_RECIPE_DATA_TYPE.String
        };
    }

    private static bool ReadBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("USE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ON", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(string value, int defaultValue)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static double ReadDouble(string value, double defaultValue)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> values,
        string tab,
        string name,
        string defaultValue)
    {
        return values.TryGetValue(CreateKey(tab, name), out var value)
            ? value
            : defaultValue;
    }

    private static string CreateKey(string tab, string name)
    {
        return $"{tab.Trim().ToUpperInvariant()}|{name.Trim().ToUpperInvariant()}";
    }

    private static string GetOrDefault(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string GetRecipeName(ST_RECIPE_DATA recipe)
    {
        return string.IsNullOrWhiteSpace(recipe.Name) ? recipe.Id : recipe.Name;
    }

    private static string FormatRecipeLine(string tab, string name, string value)
    {
        return string.Join(",", Escape(tab), Escape(name), Escape(value));
    }

    private static void ValidateSavedRecipeFile(
        string path,
        IReadOnlyDictionary<string, string> expectedValues)
    {
        var actualValues = ReadRecipeValues(path)
            .GroupBy(item => CreateKey(item.Tab, item.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var expectedValue in expectedValues)
        {
            if (!actualValues.TryGetValue(expectedValue.Key, out var actualValue))
            {
                throw new InvalidDataException($"Recipe CSV validation failed. Missing item: {expectedValue.Key}");
            }

            if (!actualValue.Equals(expectedValue.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Recipe CSV validation failed. {expectedValue.Key}: expected '{expectedValue.Value}', actual '{actualValue}'");
            }
        }
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') &&
            !value.Contains('"') &&
            !value.Contains('\r') &&
            !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string GetSafeFileName(string recipeId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(recipeId.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static void DeleteIfExists(string path)
    {
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }
    }
}







