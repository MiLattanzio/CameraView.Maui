namespace CameraView.Maui;

public sealed class CameraCaptureCapabilities
{
    internal CameraCaptureCapabilities(
        IEnumerable<CameraFrameFormat> frameFormats,
        IEnumerable<CameraResolution> captureResolutions,
        IEnumerable<CameraFrameRateRange> frameRateRanges)
    {
        FrameFormats = Array.AsReadOnly(
            frameFormats?.Distinct().ToArray() ?? []);
        CaptureResolutions = Array.AsReadOnly(
            captureResolutions?.Distinct().ToArray() ?? []);
        FrameRateRanges = Array.AsReadOnly(
            frameRateRanges?.Distinct().ToArray() ?? []);
    }

    public IReadOnlyList<CameraFrameFormat> FrameFormats { get; }

    public IReadOnlyList<CameraResolution> CaptureResolutions { get; }

    public IReadOnlyList<CameraFrameRateRange> FrameRateRanges { get; }

    public double MaximumFrameRate => FrameRateRanges.Count == 0
        ? 0
        : FrameRateRanges.Max(range => range.Maximum);

    public bool SupportsFrameFormat(CameraFrameFormat format) =>
        format == CameraFrameFormat.Native
            ? FrameFormats.Any(candidate => candidate != CameraFrameFormat.Jpeg)
            : FrameFormats.Contains(format);

    public bool SupportsCaptureResolution(CameraResolution resolution) =>
        CaptureResolutions.Any(candidate => candidate.HasSameDimensions(resolution));
}
