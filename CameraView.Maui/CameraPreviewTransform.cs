namespace CameraView.Maui;

internal readonly record struct CameraPreviewTransform(
    float ScaleX,
    float ScaleY,
    float RotationDegrees,
    int RelativeRotationDegrees);

internal static class CameraPreviewTransformCalculator
{
    internal static CameraPreviewTransform Calculate(
        int viewWidth,
        int viewHeight,
        int previewWidth,
        int previewHeight,
        int sensorOrientationDegrees,
        int displayRotationDegrees,
        bool isFrontFacing)
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
        var swapsAxes = relativeRotation % 180 != 0;

        float defaultScaleX;
        float defaultScaleY;

        // TextureView already applies the sensor orientation, but stretches the
        // result to its bounds. These factors describe that implicit scaling so
        // the matrix below can undo it before applying a uniform aspect-fill.
        if (sensorOrientation % 180 == 0)
        {
            defaultScaleX = swapsAxes
                ? (float)viewWidth / previewWidth
                : (float)viewWidth / previewHeight;
            defaultScaleY = swapsAxes
                ? (float)viewHeight / previewHeight
                : (float)viewHeight / previewWidth;
        }
        else
        {
            defaultScaleX = swapsAxes
                ? (float)viewWidth / previewHeight
                : (float)viewWidth / previewWidth;
            defaultScaleY = swapsAxes
                ? (float)viewHeight / previewWidth
                : (float)viewHeight / previewHeight;
        }

        var aspectFillScale = Math.Max(defaultScaleX, defaultScaleY);
        float scaleX;
        float scaleY;

        if (swapsAxes)
        {
            scaleX = aspectFillScale / defaultScaleX;
            scaleY = aspectFillScale / defaultScaleY;
        }
        else
        {
            scaleX = (float)viewHeight / viewWidth / defaultScaleY * aspectFillScale;
            scaleY = (float)viewWidth / viewHeight / defaultScaleX * aspectFillScale;
        }

        return new CameraPreviewTransform(
            scaleX,
            scaleY,
            -displayRotation,
            relativeRotation);
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
