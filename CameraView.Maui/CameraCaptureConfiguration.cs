namespace CameraView.Maui;

public sealed class CameraCaptureConfiguration
{
    internal CameraCaptureConfiguration(
        CameraCaptureOptions requestedOptions,
        CameraResolution captureResolution,
        CameraResolution previewResolution,
        int? jpegQuality,
        TimeSpan minimumFrameInterval)
    {
        RequestedOptions = requestedOptions;
        CaptureResolution = captureResolution;
        PreviewResolution = previewResolution;
        JpegQuality = jpegQuality;
        MinimumFrameInterval = minimumFrameInterval;
    }

    public CameraCaptureOptions RequestedOptions { get; }

    public CameraResolution CaptureResolution { get; }

    public CameraResolution PreviewResolution { get; }

    public int? JpegQuality { get; }

    public TimeSpan MinimumFrameInterval { get; }

    public double MaximumFrameRate => MinimumFrameInterval > TimeSpan.Zero
        ? 1d / MinimumFrameInterval.TotalSeconds
        : 0;

    public bool UsedResolutionFallback =>
        !RequestedOptions.PreferredResolution.IsDefault &&
        !RequestedOptions.PreferredResolution.HasSameDimensions(CaptureResolution);
}
