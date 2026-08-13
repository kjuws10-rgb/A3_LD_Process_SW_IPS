namespace Drilling.Common.Review;

public static class CReviewSampleRuleSelector
{
    public static IReadOnlyCollection<string> SelectEdgeHoleKeys(ST_REVIEW_PLAN plan)
    {
        string SelectPoint1(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey;
        }

        return plan.Points
            .Where(IsCellEdgeHole)
            .Select(SelectPoint1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyCollection<string> SelectCenterHoleKeys(ST_REVIEW_PLAN plan)
    {
        string SelectPoint2(ST_REVIEW_PLAN_POINT point)
        {
            return point.HoleKey;
        }

        return plan.Points
            .Where(IsCellCenterHole)
            .Select(SelectPoint2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsCellEdgeHole(ST_REVIEW_PLAN_POINT point)
    {
        var columnCount = Math.Max(1, point.PixelCountX);
        var rowCount = Math.Max(1, point.PixelCountY);
        var zeroBasedHoleNo = Math.Max(0, point.HoleNo - 1);
        var column = zeroBasedHoleNo % columnCount;
        var row = zeroBasedHoleNo / columnCount;

        return column == 0 ||
               column == columnCount - 1 ||
               row == 0 ||
               row == rowCount - 1;
    }

    private static bool IsCellCenterHole(ST_REVIEW_PLAN_POINT point)
    {
        return !IsCellEdgeHole(point);
    }
}
