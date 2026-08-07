namespace CameraView.Maui;

public sealed class CameraControlCapabilities
{
    internal CameraControlCapabilities(
        double minimumZoomFactor,
        double maximumZoomFactor,
        bool isTorchSupported,
        bool isFocusPointSupported,
        IEnumerable<CameraFocusMode> supportedFocusModes,
        double minimumExposureCompensation,
        double maximumExposureCompensation,
        double exposureCompensationStep)
    {
        MinimumZoomFactor = minimumZoomFactor;
        MaximumZoomFactor = maximumZoomFactor;
        IsTorchSupported = isTorchSupported;
        IsFocusPointSupported = isFocusPointSupported;
        SupportedFocusModes = Array.AsReadOnly(
            supportedFocusModes?.Distinct().ToArray() ?? []);
        MinimumExposureCompensation = minimumExposureCompensation;
        MaximumExposureCompensation = maximumExposureCompensation;
        ExposureCompensationStep = exposureCompensationStep;
    }

    public double MinimumZoomFactor { get; }

    public double MaximumZoomFactor { get; }

    public bool IsZoomSupported => MaximumZoomFactor > MinimumZoomFactor;

    public bool IsTorchSupported { get; }

    public bool IsFocusPointSupported { get; }

    public IReadOnlyList<CameraFocusMode> SupportedFocusModes { get; }

    public double MinimumExposureCompensation { get; }

    public double MaximumExposureCompensation { get; }

    public double ExposureCompensationStep { get; }

    public bool SupportsExposureCompensation =>
        MinimumExposureCompensation < 0 || MaximumExposureCompensation > 0;

    public bool SupportsFocusMode(CameraFocusMode mode) =>
        SupportedFocusModes.Contains(mode);
}
