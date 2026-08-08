namespace CameraView.Maui;

internal readonly record struct CameraPreviewTransform(
    int Width,
    int Height,
    int RelativeRotationDegrees,
    bool IsMirrored);

internal static class CameraPreviewTransformCalculator
{
    internal static CameraPreviewTransform Calculate(
        int viewWidth,
        int viewHeight,
        int previewWidth,
        int previewHeight,
        int sensorOrientationDegrees,
        int displayRotationDegrees,
        bool isFrontFacing) =>
        Calculate(
            viewWidth,
            viewHeight,
            previewWidth,
            previewHeight,
            sensorOrientationDegrees,
            displayRotationDegrees,
            isFrontFacing,
            false);

    internal static CameraPreviewTransform Calculate(
        int viewWidth,
        int viewHeight,
        int previewWidth,
        int previewHeight,
        int sensorOrientationDegrees,
        int displayRotationDegrees,
        bool isFrontFacing,
        bool isPreviewMirrored)
    {
        if (viewWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewWidth));
        if (viewHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewHeight));
        if (previewWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(previewWidth));
        if (previewHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(previewHeight));

        var sensorOrientation = NormalizeDegrees(sensorOrientationDegrees);
        var displayRotation = NormalizeDegrees(displayRotationDegrees);
        var relativeRotation = ComputeRelativeRotation(
            sensorOrientation,
            displayRotation,
            isFrontFacing);
        // SurfaceView applies sensor orientation and display rotation itself. Size
        // the view to the oriented source aspect and enlarge it uniformly until it
        // covers the container; the parent clips only the overflowing dimension.
        var swapsAxes = relativeRotation % 180 != 0;
        var displayedSourceWidth = swapsAxes ? previewHeight : previewWidth;
        var displayedSourceHeight = swapsAxes ? previewWidth : previewHeight;
        var aspectFillScale = Math.Max(
            viewWidth / (float)displayedSourceWidth,
            viewHeight / (float)displayedSourceHeight);

        return new CameraPreviewTransform(
            Math.Max(viewWidth, (int)Math.Ceiling(displayedSourceWidth * aspectFillScale)),
            Math.Max(viewHeight, (int)Math.Ceiling(displayedSourceHeight * aspectFillScale)),
            relativeRotation,
            isPreviewMirrored);
    }

    internal static int ComputeRelativeRotation(
        int sensorOrientationDegrees,
        int displayRotationDegrees,
        bool isFrontFacing)
    {
        var sensorOrientation = NormalizeDegrees(sensorOrientationDegrees);
        var displayRotation = NormalizeDegrees(displayRotationDegrees);
        var sign = isFrontFacing ? 1 : -1;
        return (sensorOrientation - displayRotation * sign + 360) % 360;
    }

    private static int NormalizeDegrees(int degrees) =>
        ((degrees % 360) + 360) % 360;
}
