namespace CameraView.Maui;

internal static class CameraControlNegotiator
{
    internal static CameraControlState Negotiate(
        CameraControlOptions options,
        CameraControlCapabilities capabilities,
        CameraOptions camera)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capabilities);
        options.Validate();

        var zoomFactor = Math.Clamp(
            options.ZoomFactor,
            capabilities.MinimumZoomFactor,
            capabilities.MaximumZoomFactor);
        var torchEnabled = options.TorchEnabled && capabilities.IsTorchSupported;
        var focusMode = SelectFocusMode(options.FocusMode, capabilities);
        var focusPoint = focusMode.HasValue && capabilities.IsFocusPointSupported
            ? options.FocusPoint
            : null;
        var exposureCompensation = Math.Clamp(
            options.ExposureCompensation,
            capabilities.MinimumExposureCompensation,
            capabilities.MaximumExposureCompensation);
        if (capabilities.ExposureCompensationStep > 0)
        {
            exposureCompensation = Math.Round(
                exposureCompensation / capabilities.ExposureCompensationStep,
                MidpointRounding.AwayFromZero) * capabilities.ExposureCompensationStep;
            exposureCompensation = Math.Clamp(
                exposureCompensation,
                capabilities.MinimumExposureCompensation,
                capabilities.MaximumExposureCompensation);
        }

        var isPreviewMirrored = options.PreviewMirroring switch
        {
            CameraPreviewMirroringMode.Automatic => camera == CameraOptions.Front,
            CameraPreviewMirroringMode.Mirrored => true,
            CameraPreviewMirroringMode.Unmirrored => false,
            _ => throw new ArgumentOutOfRangeException(nameof(options.PreviewMirroring))
        };

        return new CameraControlState(
            options,
            zoomFactor,
            torchEnabled,
            focusMode,
            focusPoint,
            exposureCompensation,
            isPreviewMirrored,
            capabilities);
    }

    private static CameraFocusMode? SelectFocusMode(
        CameraFocusMode requestedMode,
        CameraControlCapabilities capabilities)
    {
        if (capabilities.SupportsFocusMode(requestedMode))
            return requestedMode;
        if (capabilities.SupportsFocusMode(CameraFocusMode.Continuous))
            return CameraFocusMode.Continuous;
        if (capabilities.SupportsFocusMode(CameraFocusMode.Single))
            return CameraFocusMode.Single;
        return null;
    }
}
