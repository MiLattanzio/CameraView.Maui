namespace CameraView.Maui;

public sealed class CameraCaptureConfiguration
{
    internal CameraCaptureConfiguration(
        CameraCaptureOptions requestedOptions,
        CameraResolution captureResolution,
        CameraResolution previewResolution,
        int? jpegQuality,
        TimeSpan minimumFrameInterval,
        CameraFrameFormat frameFormat,
        CameraFrameDeliveryMode frameDeliveryMode,
        CameraFrameRateRange? nativeFrameRate,
        CameraCaptureCapabilities capabilities)
    {
        RequestedOptions = requestedOptions;
        CaptureResolution = captureResolution;
        PreviewResolution = previewResolution;
        JpegQuality = jpegQuality;
        MinimumFrameInterval = minimumFrameInterval;
        FrameFormat = frameFormat;
        FrameDeliveryMode = frameDeliveryMode;
        NativeFrameRate = nativeFrameRate;
        Capabilities = capabilities;
    }

    public CameraCaptureOptions RequestedOptions { get; }

    public CameraResolution CaptureResolution { get; }

    public CameraResolution PreviewResolution { get; }

    public int? JpegQuality { get; }

    public TimeSpan MinimumFrameInterval { get; }

    public CameraFrameFormat FrameFormat { get; }

    public CameraFrameDeliveryMode FrameDeliveryMode { get; }

    public CameraFrameRateRange? NativeFrameRate { get; }

    public CameraCaptureCapabilities Capabilities { get; }

    public double MaximumFrameRate => MinimumFrameInterval > TimeSpan.Zero
        ? 1d / MinimumFrameInterval.TotalSeconds
        : 0;

    public bool UsedResolutionFallback =>
        !RequestedOptions.PreferredResolution.IsDefault &&
        !RequestedOptions.PreferredResolution.HasSameDimensions(CaptureResolution);

    public bool UsedFrameFormatFallback =>
        RequestedOptions.FrameFormat != CameraFrameFormat.Native &&
        RequestedOptions.FrameFormat != FrameFormat;
}
