namespace CameraView.Maui;

public sealed class CameraCaptureConfiguration : EventArgs
{
    public CameraCaptureConfiguration(
        int width,
        int height,
        int jpegQuality,
        int maximumFrameRate,
        TimeSpan minimumFrameInterval)
    {
        Width = width;
        Height = height;
        JpegQuality = jpegQuality;
        MaximumFrameRate = maximumFrameRate;
        MinimumFrameInterval = minimumFrameInterval;
    }

    public int Width { get; }
    public int Height { get; }
    public int JpegQuality { get; }
    public int MaximumFrameRate { get; }
    public TimeSpan MinimumFrameInterval { get; }
}
