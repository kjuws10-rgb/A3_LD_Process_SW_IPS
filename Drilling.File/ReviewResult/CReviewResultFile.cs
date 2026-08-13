using System.Globalization;
using Drilling.Common.Review;
using Drilling.File.Parser;

namespace Drilling.File.ReviewResult;

public sealed class CReviewResultFile(string configRoot) : CReviewResultFileBase
{
    private static readonly IReadOnlyList<string> Headers =
    [
        "SAVED_AT",
        "RECIPE_ID",
        "HOLE_KEY",
        "HEAD_NO",
        "CELL_NO",
        "ERROR_X",
        "ERROR_Y",
        "JUDGE"
    ];

    private readonly string _reviewResultRoot = Path.Combine(
        Directory.GetParent(configRoot)?.FullName ?? configRoot,
        "Data",
        "ReviewResult");

    public override string RootPath
    {
        get
        {
            return _reviewResultRoot;
        }
    }

    public override ST_REVIEW_RESULT_FILE_DATA Load(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] SelectHeader1(string header)
        {
            return new[] { header };
        }

        CCsvParser.ValidateRequiredHeaders(
            path,
            "Review Result",
            Headers.Select(SelectHeader1));

        var sourceRows = CCsvParser.Read(path);
        if (sourceRows.Count == 0)
        {
            throw new InvalidDataException("Review Result validation failed. Result row is empty.");
        }
        ST_REVIEW_RESULT_FILE_ROW SelectRow2(IReadOnlyDictionary<string, string> row, int index)
        {
            return ToResultRow(row, index + 2);
        }

        var rows = sourceRows
            .Select(SelectRow2)
            .ToArray();
        string SelectRow3(ST_REVIEW_RESULT_FILE_ROW row)
        {
            return row.RecipeId;
        }

        var recipeIds = rows
            .Select(SelectRow3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (recipeIds.Length != 1)
        {
            throw new InvalidDataException(
                "Review Result validation failed. All rows must have the same RECIPE_ID.");
        }

        var fullPath = Path.GetFullPath(path);
        DateTimeOffset MaxRowCallback4(ST_REVIEW_RESULT_FILE_ROW row)
        {
            return row.SavedAt;
        }

        return new ST_REVIEW_RESULT_FILE_DATA(
            fullPath,
            Path.GetFileName(fullPath),
            recipeIds[0],
            rows.Max(MaxRowCallback4),
            rows);
    }

    public override void Save(
        ST_REVIEW_RESULT_DATA result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = result.Results.Count > 0
            ? result.Results
            : result.Plan.ReviewPoints;
        IReadOnlyDictionary<string, string> SelectPoint5(ST_REVIEW_PLAN_POINT point)
        {
            return ToRow(result, point);
        }

        CCsvParser.Write(
            GetResultPath(result),
            Headers,
            rows.Select(SelectPoint5));

        return;
    }

    private static ST_REVIEW_RESULT_FILE_ROW ToResultRow(
        IReadOnlyDictionary<string, string> row,
        int rowNo)
    {
        var savedAtText = CCsvParser.RequireText(
            row,
            "Review Result",
            rowNo,
            "SAVED_AT");
        if (!DateTimeOffset.TryParse(
                savedAtText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var savedAt))
        {
            throw new InvalidDataException(
                $"Review Result validation failed. Row {rowNo} / SAVED_AT must be a date and time.");
        }

        var recipeId = CCsvParser.RequireText(
            row,
            "Review Result",
            rowNo,
            "RECIPE_ID");
        var holeKey = CCsvParser.RequireText(
            row,
            "Review Result",
            rowNo,
            "HOLE_KEY");
        var headNo = CCsvParser.ReadRequiredInt(
            CCsvParser.Get(row, "HEAD_NO"),
            "Review Result",
            rowNo,
            "HEAD_NO");
        var cellNo = CCsvParser.ReadRequiredInt(
            CCsvParser.Get(row, "CELL_NO"),
            "Review Result",
            rowNo,
            "CELL_NO");
        var errorX = CCsvParser.ReadDouble(
            CCsvParser.Get(row, "ERROR_X"),
            "Review Result",
            rowNo,
            "ERROR_X",
            0.0);
        var errorY = CCsvParser.ReadDouble(
            CCsvParser.Get(row, "ERROR_Y"),
            "Review Result",
            rowNo,
            "ERROR_Y",
            0.0);
        var judge = CCsvParser.RequireText(
            row,
            "Review Result",
            rowNo,
            "JUDGE").ToUpperInvariant();

        return new ST_REVIEW_RESULT_FILE_ROW(
            savedAt,
            recipeId,
            holeKey,
            headNo,
            cellNo,
            errorX,
            errorY,
            judge);
    }

    private string GetResultPath(ST_REVIEW_RESULT_DATA result)
    {
        var savedAt = result.SavedAt;
        var recipeId = SanitizeFileName(result.Plan.RecipeId);

        return Path.Combine(
            _reviewResultRoot,
            savedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            $"ReviewResult_{recipeId}_{savedAt:HHmmss}.csv");
    }

    private static IReadOnlyDictionary<string, string> ToRow(
        ST_REVIEW_RESULT_DATA result,
        ST_REVIEW_PLAN_POINT point)
    {
        var plan = result.Plan;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SAVED_AT"] = FormatDate(result.SavedAt),
            ["RECIPE_ID"] = plan.RecipeId,
            ["HOLE_KEY"] = $"CELL{point.CellNo}_{point.HoleName}",
            ["HEAD_NO"] = point.HeadNo.ToString(CultureInfo.InvariantCulture),
            ["CELL_NO"] = point.CellNo.ToString(CultureInfo.InvariantCulture),
            ["ERROR_X"] = FormatDouble(point.ErrorX),
            ["ERROR_Y"] = FormatDouble(point.ErrorY),
            ["JUDGE"] = point.Judge
        };
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.000000", CultureInfo.InvariantCulture);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        char SelectCharacter6(char character)
        {
            return invalidChars.Contains(character) ? '_' : character;
        }

        var sanitized = new string(value
            .Select(SelectCharacter6)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "RECIPE" : sanitized;
    }
}
