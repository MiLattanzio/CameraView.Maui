namespace CameraView.Maui;

internal static class CameraControlPointMapper
{
    internal static CameraPoint ToSensorPoint(
        CameraPoint previewPoint,
        int viewWidth,
        int viewHeight,
        int previewWidth,
        int previewHeight,
        int relativeRotationDegrees,
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

        var rotation = NormalizeRotation(relativeRotationDegrees);
        var swapsAxes = rotation % 180 != 0;
        var rotatedWidth = swapsAxes ? previewHeight : previewWidth;
        var rotatedHeight = swapsAxes ? previewWidth : previewHeight;
        var aspectFillScale = Math.Max(
            viewWidth / (double)rotatedWidth,
            viewHeight / (double)rotatedHeight);
        var displayedWidth = rotatedWidth * aspectFillScale;
        var displayedHeight = rotatedHeight * aspectFillScale;
        var visibleX = isPreviewMirrored ? 1d - previewPoint.X : previewPoint.X;
        var uncroppedPoint = new CameraPoint(
            Math.Clamp(
                (visibleX * viewWidth + (displayedWidth - viewWidth) / 2d) /
                displayedWidth,
                0,
                1),
            Math.Clamp(
                (previewPoint.Y * viewHeight + (displayedHeight - viewHeight) / 2d) /
                displayedHeight,
                0,
                1));
        return ToSensorPoint(uncroppedPoint, rotation, false);
    }

    internal static CameraPoint ToSensorPoint(
        CameraPoint previewPoint,
        int relativeRotationDegrees,
        bool isPreviewMirrored)
    {
        var x = isPreviewMirrored ? 1d - previewPoint.X : previewPoint.X;
        var y = previewPoint.Y;
        var rotation = NormalizeRotation(relativeRotationDegrees);
        return rotation switch
        {
            0 => new CameraPoint(x, y),
            90 => new CameraPoint(y, 1d - x),
            180 => new CameraPoint(1d - x, 1d - y),
            270 => new CameraPoint(1d - y, x),
            _ => throw new ArgumentOutOfRangeException(
                nameof(relativeRotationDegrees),
                "Camera rotation must be a multiple of 90 degrees.")
        };
    }

    private static int NormalizeRotation(int degrees) =>
        ((degrees % 360) + 360) % 360;
}
