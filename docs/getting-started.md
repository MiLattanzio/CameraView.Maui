# Getting started

## 1. Install the package

Add CameraView.Maui to the MAUI application project:

```shell
dotnet add package CameraView.Maui
```

The application must target a supported .NET 9 or .NET 10 MAUI Android or iOS framework.

## 2. Register the handler

Call `UseCameraView` while creating the MAUI app:

```csharp
using CameraView.Maui;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .UseCameraView();

    return builder.Build();
}
```

The control cannot create its native view until this handler is registered.

## 3. Configure platform permissions

Android permission metadata is merged from the package. On iOS, add `NSCameraUsageDescription` to the app's `Info.plist`:

```xml
<key>NSCameraUsageDescription</key>
<string>The camera is used to capture images.</string>
```

See [Platform setup](platform-setup.md) for details.

## 4. Add the control

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:camera="clr-namespace:CameraView.Maui;assembly=CameraView.Maui"
    x:Class="MyApp.CameraPage">

    <camera:CameraView
        x:Name="CameraPreview"
        Camera="Rear"
        Orientation="Portrait"
        HorizontalOptions="Fill"
        VerticalOptions="Fill" />
</ContentPage>
```

## 5. Receive frames

```csharp
public CameraPage()
{
    InitializeComponent();
    CameraPreview.OnFrameResult += OnFrameResult;
}

private void OnFrameResult(CameraResult result)
{
    if (!result.Success || result.Image is null)
        return;

    var jpeg = result.Image;
    MainThread.BeginInvokeOnMainThread(() =>
    {
        StatusLabel.Text = $"Received {jpeg.Length:N0} bytes";
    });
}
```

Frame callbacks run on a native capture queue. Marshal UI work to the MAUI main thread and keep the callback short so the camera pipeline is not delayed.

## 6. Configure capture

Replace `CaptureOptions` to apply a complete configuration with one session restart:

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Balanced with
{
    PreferredResolution = new CameraResolution(1600, 1200),
    ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost,
    JpegQuality = 82,
    MaximumFrameRate = 12.5,
    MinimumFrameInterval = TimeSpan.FromMilliseconds(50)
};
```

The longer of the frame-rate interval and `MinimumFrameInterval` is enforced. Use `CameraCaptureOptions.Default` to retain the 1.0 behavior, including the Android platform JPEG default and 720p-or-lower size negotiation.

Inspect the selected hardware configuration on the UI thread:

```csharp
CameraPreview.EffectiveConfigurationChanged += (_, args) =>
{
    if (args.Configuration is { } selected)
    {
        ConfigurationLabel.Text =
            $"Capture {selected.CaptureResolution}, " +
            $"preview {selected.PreviewResolution}";
    }
};
```

`Closest`, `AtMost`, and `AtLeast` provide deterministic fallback. Choose `Exact` when a different size must be treated as a configuration failure.

## High-throughput frame processing

JPEG remains the default. For OCR, barcode, or ML processors that accept luminance/YUV input, switch to the realtime profile and use `FrameAvailable`:

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Realtime;
CameraPreview.FrameAvailable += OnFrameAvailable;

private void OnFrameAvailable(object? sender, CameraFrameEventArgs args)
{
    CameraFrame frame = args.Frame;
    CameraFramePlane yPlane = frame.Planes[0];
    ProcessLuminance(
        yPlane.Span,
        yPlane.Width,
        yPlane.Height,
        yPlane.RowStride,
        frame.RotationDegrees);
}
```

The event frame is borrowed and valid only during the handler. For asynchronous processing, create and dispose a retained lease:

```csharp
private async void OnFrameAvailable(object? sender, CameraFrameEventArgs args)
{
    using CameraFrame retained = args.Frame.Retain();
    await Task.Run(() => ProcessFrame(retained));
}
```

Do not retain an unbounded number of frames. `Latest` delivery prioritizes the newest camera buffer, while `MaxOutstandingFrames` limits native memory pressure. Read `EffectiveConfiguration.FrameFormat`, `NativeFrameRate`, and `Capabilities` to see what the selected camera actually supports.

## 7. Configure interactive controls

Replace `ControlOptions` to update controls without restarting the preview:

```csharp
CameraPreview.ControlOptions = CameraPreview.ControlOptions with
{
    ZoomFactor = 2,
    TorchEnabled = true,
    ExposureCompensation = 0.5,
    FocusMode = CameraFocusMode.Continuous,
    PreviewMirroring = CameraPreviewMirroringMode.Automatic
};
```

Use a normalized point for tap-to-focus:

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

Read `EffectiveControls` before enabling UI controls. Requests are portable across cameras: unsupported torch/focus modes fall back, while zoom and exposure are clamped to the selected device.

```csharp
CameraPreview.EffectiveControlsChanged += (_, args) =>
{
    if (args.State is not { } controls)
        return;

    ZoomSlider.Minimum = controls.Capabilities.MinimumZoomFactor;
    ZoomSlider.Maximum = controls.Capabilities.MaximumZoomFactor;
    ZoomSlider.IsEnabled = controls.Capabilities.IsZoomSupported;
    TorchButton.IsEnabled = controls.Capabilities.IsTorchSupported;
    ExposureSlider.Minimum = controls.Capabilities.MinimumExposureCompensation;
    ExposureSlider.Maximum = controls.Capabilities.MaximumExposureCompensation;
    ExposureSlider.IsEnabled = controls.Capabilities.SupportsExposureCompensation;
};
```

`PreviewMirroring` affects only the preview. Inspect `CameraFrame.IsMirrored` when processing or displaying delivered frames.

## 8. Observe camera state and errors

`StateChanged` and `ErrorOccurred` run through the MAUI dispatcher, so their handlers can update page controls directly:

```csharp
public CameraPage()
{
    InitializeComponent();
    CameraPreview.StateChanged += OnCameraStateChanged;
    CameraPreview.ErrorOccurred += OnCameraError;
}

private void OnCameraStateChanged(
    object sender,
    CameraStateChangedEventArgs args)
{
    StateLabel.Text = $"{args.State} ({args.Camera})";
}

private void OnCameraError(object sender, CameraErrorEventArgs args)
{
    ErrorLabel.Text = $"{args.Code}: {args.Message}";

    if (!args.IsRecoverable)
        RetryButton.IsEnabled = false;
}
```

Use `CameraPreview.State` for the current state and `CameraPreview.IsRunning` when only an active-session check is required. `Camera` remains the requested and selected camera position.

## Camera state

Switch cameras:

```csharp
CameraPreview.Camera = CameraOptions.Front;
```

Change output orientation:

```csharp
CameraPreview.Orientation = CameraOrientation.Landscape;
```

Stop and restart capture:

```csharp
CameraPreview.Enabled = false;
CameraPreview.Enabled = true;
```

When the app loses activation, the control releases the native camera. It automatically restores the configured camera after activation if `Enabled` is still `true`.
