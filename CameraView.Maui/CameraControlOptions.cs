namespace CameraView.Maui;

public sealed record CameraControlOptions
{
    public static CameraControlOptions Default { get; } = new();

    public double ZoomFactor { get; init; } = 1;

    public bool TorchEnabled { get; init; }

    public CameraFocusMode FocusMode { get; init; } = CameraFocusMode.Continuous;

    public CameraPoint? FocusPoint { get; init; }

    public double ExposureCompensation { get; init; }

    public CameraPreviewMirroringMode PreviewMirroring { get; init; } =
        CameraPreviewMirroringMode.Automatic;

    internal void Validate()
    {
        if (!double.IsFinite(ZoomFactor) || ZoomFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(ZoomFactor), "Zoom factor must be positive and finite.");
        if (!Enum.IsDefined(FocusMode))
            throw new ArgumentOutOfRangeException(nameof(FocusMode));
        if (!double.IsFinite(ExposureCompensation))
            throw new ArgumentOutOfRangeException(nameof(ExposureCompensation), "Exposure compensation must be finite.");
        if (!Enum.IsDefined(PreviewMirroring))
            throw new ArgumentOutOfRangeException(nameof(PreviewMirroring));
    }
}
