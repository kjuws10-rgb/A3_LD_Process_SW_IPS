namespace Drilling.Common.Review;

[Flags]
public enum EN_VISION_AXIS_MODE
{
    Normal = 0,
    XFlip = 1,
    YFlip = 2,
    XyFlip = 4
}

public readonly record struct ST_REVIEW_COORDINATE_OFFSET(
    double X,
    double Y);

public readonly record struct ST_REVIEW_COORDINATE_FORMULA(
    string Gx,
    string Gy);

public static class CReviewCoordinateTransformer
{
    public static ST_REVIEW_COORDINATE_OFFSET VisionErrorToScannerOffset(
        double visionErrorX,
        double visionErrorY,
        int headNo,
        EN_VISION_AXIS_MODE axisMode)
    {
        var transformedX = visionErrorX;
        var transformedY = visionErrorY;

        if (axisMode.HasFlag(EN_VISION_AXIS_MODE.XFlip))
        {
            transformedX = -transformedX;
        }

        if (axisMode.HasFlag(EN_VISION_AXIS_MODE.YFlip))
        {
            transformedY = -transformedY;
        }

        if (axisMode.HasFlag(EN_VISION_AXIS_MODE.XyFlip))
        {
            (transformedX, transformedY) = (-transformedY, -transformedX);
        }

        return new ST_REVIEW_COORDINATE_OFFSET(
            headNo > 0 && headNo % 2 != 0 ? -transformedX : transformedX,
            headNo > 0 && headNo % 2 == 0 ? -transformedY : transformedY);
    }

    public static ST_REVIEW_COORDINATE_FORMULA VisionErrorToScannerFormula(
        int headNo,
        EN_VISION_AXIS_MODE axisMode)
    {
        var errorXBasis = VisionErrorToScannerOffset(1.0, 0.0, headNo, axisMode);
        var errorYBasis = VisionErrorToScannerOffset(0.0, 1.0, headNo, axisMode);

        return new ST_REVIEW_COORDINATE_FORMULA(
            FormatFormula(errorXBasis.X, errorYBasis.X),
            FormatFormula(errorXBasis.Y, errorYBasis.Y));
    }

    public static EN_VISION_AXIS_MODE CreateVisionAxisMode(
        bool xFlip,
        bool yFlip,
        bool xyFlip)
    {
        var axisMode = EN_VISION_AXIS_MODE.Normal;

        if (xFlip)
        {
            axisMode |= EN_VISION_AXIS_MODE.XFlip;
        }

        if (yFlip)
        {
            axisMode |= EN_VISION_AXIS_MODE.YFlip;
        }

        if (xyFlip)
        {
            axisMode |= EN_VISION_AXIS_MODE.XyFlip;
        }

        return axisMode;
    }

    public static EN_VISION_AXIS_MODE ParseVisionAxisMode(
        string? xFlipValue,
        string? yFlipValue,
        string? xyFlipValue)
    {
        return CreateVisionAxisMode(
            ParseOnOff(xFlipValue),
            ParseOnOff(yFlipValue),
            ParseOnOff(xyFlipValue));
    }

    public static string FormatVisionAxisMode(EN_VISION_AXIS_MODE axisMode)
    {
        return $"X {FormatOnOff(axisMode.HasFlag(EN_VISION_AXIS_MODE.XFlip))} / " +
            $"Y {FormatOnOff(axisMode.HasFlag(EN_VISION_AXIS_MODE.YFlip))} / " +
            $"XY {FormatOnOff(axisMode.HasFlag(EN_VISION_AXIS_MODE.XyFlip))}";
    }

    private static bool ParseOnOff(string? value)
    {
        return (value ?? "").Trim().ToUpperInvariant() is
            "ON" or "TRUE" or "1" or "YES";
    }

    private static string FormatOnOff(bool value)
    {
        return value ? "ON" : "OFF";
    }

    private static string FormatFormula(double errorXCoefficient, double errorYCoefficient)
    {
        if (Math.Abs(errorXCoefficient) > 0.5)
        {
            return errorXCoefficient > 0.0 ? "+Error X" : "-Error X";
        }

        return errorYCoefficient > 0.0 ? "+Error Y" : "-Error Y";
    }
}
