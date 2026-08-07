# Interactive camera controls

CameraView.Maui 1.3.0 exposes zoom, torch, focus, exposure compensation, and preview mirroring through one immutable `CameraControlOptions` value. Replacing `ControlOptions` updates the active native camera without rebuilding the capture session.

## Requested and effective values

`ControlOptions` is a portable request. `EffectiveControls` is the state actually applied to the selected camera. This separation lets the same options survive rear/front switching and application resume even when the cameras expose different hardware.

```csharp
CameraPreview.ControlOptions = CameraControlOptions.Default with
{
    ZoomFactor = 2,
    TorchEnabled = true,
    FocusMode = CameraFocusMode.Continuous,
    ExposureCompensation = 0.5,
    PreviewMirroring = CameraPreviewMirroringMode.Automatic
};
```

Universal invalid values, such as a non-positive zoom factor, are rejected immediately. Valid hardware-specific requests follow a deterministic contract:

- zoom and exposure compensation are clamped to the reported range;
- exposure is quantized when the platform reports a non-zero step;
- unsupported torch requests become off;
- unsupported focus modes fall back to a supported mode, or `null` when none is available;
- focus points become `null` when native point-of-interest metering is unavailable.

The `UsedZoomFallback`, `UsedTorchFallback`, `UsedFocusFallback`, and `UsedExposureFallback` properties identify every negotiated difference.

## Build UI from capabilities

Do not hard-code a zoom maximum or assume the front camera has a torch. `EffectiveControlsChanged` runs through the MAUI dispatcher, so its handler can configure UI directly.

```csharp
CameraPreview.EffectiveControlsChanged += (_, args) =>
{
    if (args.State is not { } controls)
        return;

    var capabilities = controls.Capabilities;

    ZoomSlider.Minimum = capabilities.MinimumZoomFactor;
    ZoomSlider.Maximum = capabilities.MaximumZoomFactor;
    ZoomSlider.Value = controls.ZoomFactor;
    ZoomSlider.IsEnabled = capabilities.IsZoomSupported;

    ExposureSlider.Minimum = capabilities.MinimumExposureCompensation;
    ExposureSlider.Maximum = capabilities.MaximumExposureCompensation;
    ExposureSlider.Value = controls.ExposureCompensation;
    ExposureSlider.IsEnabled = capabilities.SupportsExposureCompensation;

    TorchButton.IsEnabled = capabilities.IsTorchSupported;
};
```

Replace the options from UI callbacks:

```csharp
private void OnZoomChanged(object sender, ValueChangedEventArgs args)
{
    CameraPreview.ControlOptions = CameraPreview.ControlOptions with
    {
        ZoomFactor = args.NewValue
    };
}
```

Rapid slider updates replace the repeating Camera2 request or update the locked AVFoundation device; they do not restart the preview. Applications with high-frequency gesture input can coalesce values to the display refresh rate to avoid redundant native updates.

## Tap-to-focus

`CameraPoint` is normalized against the visible preview: `(0,0)` is top-left and `(1,1)` is bottom-right. The platforms account for preview rotation, mirroring, and aspect-fill cropping when mapping to native camera coordinates.

Add a gesture recognizer:

```xml
<camera:CameraView x:Name="CameraPreview">
    <camera:CameraView.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnPreviewTapped" />
    </camera:CameraView.GestureRecognizers>
</camera:CameraView>
```

Normalize the gesture position and request one autofocus operation:

```csharp
private void OnPreviewTapped(object sender, TappedEventArgs args)
{
    var position = args.GetPosition(CameraPreview);
    if (!position.HasValue || CameraPreview.Width <= 0 || CameraPreview.Height <= 0)
        return;

    CameraPreview.ControlOptions = CameraPreview.ControlOptions with
    {
        FocusMode = CameraFocusMode.Single,
        FocusPoint = new CameraPoint(
            Math.Clamp(position.Value.X / CameraPreview.Width, 0, 1),
            Math.Clamp(position.Value.Y / CameraPreview.Height, 0, 1))
    };
}
```

Restore centered continuous autofocus with:

```csharp
CameraPreview.ControlOptions = CameraPreview.ControlOptions with
{
    FocusMode = CameraFocusMode.Continuous,
    FocusPoint = null
};
```

Android configures bounded AF and, when supported, AE metering regions. iOS uses the preview layer to obtain an AVFoundation point of interest.

## Torch and camera switching

Torch is a continuous light, not a one-frame flash. A `true` request is retained even when the current camera cannot satisfy it. For example, switching from a rear camera with torch enabled to an unsupported front camera reports effective `TorchEnabled == false`; switching back can restore the original request.

Use the effective value for labels and the requested value when implementing a toggle:

```csharp
CameraPreview.ControlOptions = CameraPreview.ControlOptions with
{
    TorchEnabled = !CameraPreview.ControlOptions.TorchEnabled
};
```

## Exposure compensation

`ExposureCompensation` is expressed in EV. Read the native minimum, maximum, and step from `CameraControlCapabilities`. Android devices commonly expose discrete steps; iOS normally accepts a continuous value within its bias range.

Exposure compensation does not select manual ISO or shutter duration. Automatic exposure remains active and treats this value as a brightness target bias.

## Preview mirroring and frame output

`CameraPreviewMirroringMode.Automatic` mirrors the front preview and leaves the rear preview unmirrored. `Mirrored` and `Unmirrored` explicitly override that choice.

This option changes only the native preview. It never copies or rewrites raw buffers. Delivered frame behavior remains platform-specific and is stated by `CameraFrame.IsMirrored`; processors and custom renderers must use that property rather than assuming it matches the preview.

## Lifecycle and failures

The requested controls are reapplied after camera switching, orientation reconfiguration, screen lock, window deactivation, and resume. Capabilities and effective values are recalculated for every new selected camera.

Unsupported values are normal fallbacks and do not raise errors. A native failure while applying otherwise valid controls raises `CameraErrorCode.ControlConfigurationFailed` through `ErrorOccurred` without stopping an otherwise healthy capture session. Log the requested/effective state and `PlatformCode` when reporting such a device-specific failure.
