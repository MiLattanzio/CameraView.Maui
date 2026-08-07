namespace CameraView.Maui;

public sealed class CameraControlState
{
    internal CameraControlState(
        CameraControlOptions requestedOptions,
        double zoomFactor,
        bool torchEnabled,
        CameraFocusMode? focusMode,
        CameraPoint? focusPoint,
        double exposureCompensation,
        bool isPreviewMirrored,
        CameraControlCapabilities capabilities)
    {
        RequestedOptions = requestedOptions;
        ZoomFactor = zoomFactor;
        TorchEnabled = torchEnabled;
        FocusMode = focusMode;
        FocusPoint = focusPoint;
        ExposureCompensation = exposureCompensation;
        IsPreviewMirrored = isPreviewMirrored;
        Capabilities = capabilities;
    }

    public CameraControlOptions RequestedOptions { get; }

    public double ZoomFactor { get; }

    public bool TorchEnabled { get; }

    public CameraFocusMode? FocusMode { get; }

    public CameraPoint? FocusPoint { get; }

    public double ExposureCompensation { get; }

    public bool IsPreviewMirrored { get; }

    public CameraControlCapabilities Capabilities { get; }

    public bool UsedZoomFallback => ZoomFactor != RequestedOptions.ZoomFactor;

    public bool UsedTorchFallback => TorchEnabled != RequestedOptions.TorchEnabled;

    public bool UsedFocusFallback =>
        FocusMode != RequestedOptions.FocusMode || FocusPoint != RequestedOptions.FocusPoint;

    public bool UsedExposureFallback =>
        ExposureCompensation != RequestedOptions.ExposureCompensation;
}
