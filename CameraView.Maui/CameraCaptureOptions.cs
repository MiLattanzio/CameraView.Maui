namespace CameraView.Maui;

public sealed record CameraCaptureOptions
{
    public static CameraCaptureOptions Default { get; } = new();

    public static CameraCaptureOptions LowBandwidth { get; } = new()
    {
        PreferredResolution = CameraResolution.Vga,
        JpegQuality = 70,
        MaximumFrameRate = 10
    };

    public static CameraCaptureOptions Balanced { get; } = new()
    {
        PreferredResolution = CameraResolution.Hd720p,
        JpegQuality = 85,
        MaximumFrameRate = 15
    };

    public static CameraCaptureOptions HighQuality { get; } = new()
    {
        PreferredResolution = CameraResolution.Hd1080p,
        JpegQuality = 92
    };

    public static CameraCaptureOptions Realtime { get; } = new()
    {
        PreferredResolution = CameraResolution.Hd720p,
        FrameFormat = CameraFrameFormat.Native,
        FrameDeliveryMode = CameraFrameDeliveryMode.Latest,
        FrameRateMode = CameraFrameRateMode.Maximum,
        MaxOutstandingFrames = 3
    };

    public CameraResolution PreferredResolution { get; init; } = CameraResolution.Default;

    public CameraResolutionSelectionMode ResolutionSelectionMode { get; init; } =
        CameraResolutionSelectionMode.Closest;

    public int? JpegQuality { get; init; }

    public CameraFrameFormat FrameFormat { get; init; } = CameraFrameFormat.Jpeg;

    public CameraFrameDeliveryMode FrameDeliveryMode { get; init; } =
        CameraFrameDeliveryMode.Latest;

    public int MaxOutstandingFrames { get; init; } = 2;

    public CameraFrameRateMode FrameRateMode { get; init; } =
        CameraFrameRateMode.PlatformDefault;

    public double TargetFrameRate { get; init; }

    public double MaximumFrameRate { get; init; }

    public TimeSpan MinimumFrameInterval { get; init; } = TimeSpan.Zero;

    internal TimeSpan GetEffectiveMinimumFrameInterval()
    {
        var frameRateInterval = MaximumFrameRate > 0
            ? TimeSpan.FromSeconds(1d / MaximumFrameRate)
            : TimeSpan.Zero;
        return frameRateInterval > MinimumFrameInterval
            ? frameRateInterval
            : MinimumFrameInterval;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(ResolutionSelectionMode))
            throw new ArgumentOutOfRangeException(nameof(ResolutionSelectionMode));
        if (!Enum.IsDefined(FrameFormat))
            throw new ArgumentOutOfRangeException(nameof(FrameFormat));
        if (!Enum.IsDefined(FrameDeliveryMode))
            throw new ArgumentOutOfRangeException(nameof(FrameDeliveryMode));
        if (!Enum.IsDefined(FrameRateMode))
            throw new ArgumentOutOfRangeException(nameof(FrameRateMode));
        if (JpegQuality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(JpegQuality), "JPEG quality must be between 1 and 100.");
        if (MaxOutstandingFrames is < 2 or > 8)
            throw new ArgumentOutOfRangeException(
                nameof(MaxOutstandingFrames),
                "Outstanding frame capacity must be between 2 and 8.");
        if (!double.IsFinite(TargetFrameRate) || TargetFrameRate < 0 || TargetFrameRate > 1000)
            throw new ArgumentOutOfRangeException(
                nameof(TargetFrameRate),
                "Target frame rate must be between 0 and 1000.");
        if (FrameRateMode == CameraFrameRateMode.Closest && TargetFrameRate <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(TargetFrameRate),
                "Closest frame-rate selection requires a positive target.");
        if (!double.IsFinite(MaximumFrameRate) || MaximumFrameRate < 0 || MaximumFrameRate > 1000)
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameRate), "Maximum frame rate must be between 0 and 1000.");
        if (MinimumFrameInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MinimumFrameInterval));
    }
}
