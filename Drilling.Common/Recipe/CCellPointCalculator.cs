namespace Drilling.Common.Recipe;

public sealed record ST_CELL_POINT_INPUT(
    int CellNo,
    double FirstPixelX,
    double FirstPixelY,
    double RotationDegrees,
    int PixelCountX,
    int PixelCountY,
    double PitchX,
    double PitchY,
    double OriginX = 0.0,
    double OriginY = 0.0);

public sealed record ST_CELL_DRILL_POINT(
    int CellNo,
    int PointNo,
    int Column,
    int Row,
    double X,
    double Y);

public sealed record ST_CELL_POINT_RESULT(
    IReadOnlyList<ST_CELL_DRILL_POINT> Points,
    string ValidationMessage)
{
    public bool IsValid => string.IsNullOrWhiteSpace(ValidationMessage);
}

public static class CCellPointCalculator
{
    public const int MaxPointCountPerCell = 1_000_000;

    public static ST_CELL_POINT_RESULT Calculate(ST_CELL_POINT_INPUT input)
    {
        if (input.CellNo <= 0)
        {
            return Invalid("Cell number must be greater than zero.");
        }

        if (input.PixelCountX <= 0 || input.PixelCountY <= 0)
        {
            return Invalid($"Cell{input.CellNo}: Hole count X/Y must be greater than zero.");
        }

        if (input.PitchX < 0 || input.PitchY < 0)
        {
            return Invalid($"Cell{input.CellNo}: Pitch X/Y cannot be negative.");
        }

        var pointCount = (long)input.PixelCountX * input.PixelCountY;
        if (pointCount > MaxPointCountPerCell)
        {
            return Invalid($"Cell{input.CellNo}: Hole count {pointCount:N0} exceeds the limit {MaxPointCountPerCell:N0}.");
        }

        var radians = input.RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var points = new List<ST_CELL_DRILL_POINT>((int)pointCount);
        var pointNo = 1;

        for (var row = 0; row < input.PixelCountY; row++)
        {
            for (var column = 0; column < input.PixelCountX; column++)
            {
                var localX = column * input.PitchX;
                var localY = row * input.PitchY;
                var x = input.OriginX + input.FirstPixelX + (localX * cos) - (localY * sin);
                var y = input.OriginY + input.FirstPixelY + (localX * sin) + (localY * cos);

                points.Add(new ST_CELL_DRILL_POINT(
                    input.CellNo,
                    pointNo++,
                    column,
                    row,
                    x,
                    y));
            }
        }

        return new ST_CELL_POINT_RESULT(points, "");
    }

    private static ST_CELL_POINT_RESULT Invalid(string message)
    {
        return new ST_CELL_POINT_RESULT([], message);
    }
}
