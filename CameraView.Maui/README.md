# CameraView.Maui

CameraView.Maui is a .NET MAUI camera preview control for Android and iOS. It uses Camera2 and AVFoundation and emits encoded JPEG frames or zero-copy native camera buffers.

The package supports .NET 9 and .NET 10 MAUI applications.

## Supported platforms

| Platform | Minimum version |
| --- | --- |
| Android | API 28 |
| iOS | 15.0 |

## Installation

```shell
dotnet add package CameraView.Maui
```

Register the handler in `MauiProgram.cs`:

```csharp
using CameraView.Maui;

builder
    .UseMauiApp<App>()
    .UseCameraView();
```

## XAML usage

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:camera="clr-namespace:CameraView.Maui;assembly=CameraView.Maui">

    <camera:CameraView
        x:Name="CameraPreview"
        Camera="Rear"
        Orientation="Portrait" />
</ContentPage>
```

Subscribe to `OnFrameResult` to receive JPEG frames. The callback runs on a native capture queue, so marshal UI updates to the main thread:

```csharp
private void OnFrameResult(CameraResult result)
{
    if (!result.Success || result.Image is null)
        return;

    byte[] jpeg = result.Image;

    MainThread.BeginInvokeOnMainThread(() =>
    {
        StatusLabel.Text = $"Received {jpeg.Length:N0} bytes";
    });
}
```

Subscribe and unsubscribe with the page lifecycle:

```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    CameraPreview.OnFrameResult += OnFrameResult;
    CameraPreview.Enabled = true;
}

protected override void OnDisappearing()
{
    CameraPreview.Enabled = false;
    CameraPreview.OnFrameResult -= OnFrameResult;
    base.OnDisappearing();
}
```

`CameraResult.Image` is a complete JPEG byte array. It can be saved directly:

```csharp
var path = Path.Combine(FileSystem.CacheDirectory, "camera-frame.jpg");
await File.WriteAllBytesAsync(path, result.Image);
```

## High-throughput raw frames

Use the opt-in realtime profile for OCR, barcode, computer-vision, and ML workloads that can consume luminance or YUV data directly:

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Realtime;
CameraPreview.FrameAvailable += (_, args) =>
{
    CameraFrame frame = args.Frame;
    CameraFramePlane yPlane = frame.Planes[0];
    AnalyzeLuminance(
        yPlane.Span,
        yPlane.Width,
        yPlane.Height,
        yPlane.RowStride,
        frame.RotationDegrees);
};
```

The event frame is borrowed. Access it synchronously, or call `Retain()` before asynchronous work and dispose the retained frame afterward. `Native` resolves to YUV on both platforms: Android supplies three `YUV_420_888` planes and iOS supplies two NV12 planes. Inspect `EffectiveConfiguration.Capabilities` before requesting optional formats such as `Bgra8888`.

## Processing frames

Frames can arrive faster than OCR, barcode, ML, upload, or disk processing can finish. Avoid starting an unlimited number of tasks. This gate drops incoming frames while one frame is being processed:

```csharp
private readonly SemaphoreSlim _frameGate = new(1, 1);

private async void OnFrameResult(CameraResult result)
{
    if (!result.Success ||
        result.Image is null ||
        !_frameGate.Wait(0))
        return;

    try
    {
        using var stream = new MemoryStream(result.Image, writable: false);

        // Pass stream or result.Image to the selected processor.
        await ProcessJpegAsync(stream);
    }
    catch (Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(
            $"Frame processing failed: {exception}");
    }
    finally
    {
        _frameGate.Release();
    }
}

private static Task ProcessJpegAsync(Stream jpeg)
{
    // Call the selected OCR, barcode, ML, upload, or decoder API.
    return Task.CompletedTask;
}
```

For “latest frame wins” processing, use a bounded `Channel<byte[]>` with capacity one and `BoundedChannelFullMode.DropOldest`. The [full example](https://github.com/MiLattanzio/CameraView.Maui#process-frames-without-building-a-backlog) includes lifecycle cancellation.

CameraView.Maui does not force a specific decoder. If the processor needs pixels instead of JPEG bytes, decode off the UI thread with a mobile-compatible image library, dispose decoder resources promptly, and move only final UI updates to `MainThread`.

Do not render every callback into another MAUI `Image`: `CameraView` already displays a native live preview, and a second decode adds avoidable CPU and allocation pressure.

## Camera state

```csharp
CameraPreview.Camera = CameraOptions.Front;
CameraPreview.Orientation = CameraOrientation.Landscape;
CameraPreview.Enabled = false;
```

Changing `Camera` or `Orientation` reconfigures the native session. Set `Enabled` to `false` when preview or capture is not required. The control releases the camera while the app is inactive and restarts it when the app resumes if `Enabled` remains `true`.

Observe the actual native state and structured failures:

```csharp
CameraPreview.StateChanged += (_, args) =>
    StateLabel.Text = $"{args.State} ({args.Camera})";

CameraPreview.ErrorOccurred += (_, args) =>
    ErrorLabel.Text = $"{args.Code}: {args.Message}";
```

`StateChanged` and `ErrorOccurred` run through the MAUI dispatcher. `State` can be `Stopped`, `Starting`, `Running`, `Suspended`, `PermissionDenied`, or `Failed`; `IsRunning` is true only for an active native session. `OnFrameResult` and `FrameAvailable` remain on the native capture queue.

## Capture configuration

Apply resolution, quality, and delivery-rate changes together:

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Balanced with
{
    PreferredResolution = new CameraResolution(1600, 1200),
    ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost,
    JpegQuality = 82,
    MaximumFrameRate = 12.5
};

CameraPreview.EffectiveConfigurationChanged += (_, args) =>
{
    if (args.Configuration is { } selected)
        StatusLabel.Text = $"{selected.CaptureResolution}";
};
```

Use `Closest`, `AtMost`, or `AtLeast` for deterministic fallback. `Exact` fails with `SessionConfigurationFailed` when the device does not expose the requested size. `CameraCaptureOptions.Default` retains the platform JPEG default and 720p-or-lower selection used by 1.0.

## Permissions

The package contributes `android.permission.CAMERA` and the optional `android.hardware.camera.any` feature to the merged Android manifest.

iOS applications must add a user-facing description to `Info.plist`:

```xml
<key>NSCameraUsageDescription</key>
<string>The camera is used to capture images.</string>
```

## More information

Full documentation, troubleshooting, and release notes are available in the [GitHub repository](https://github.com/MiLattanzio/CameraView.Maui/tree/master/docs).
