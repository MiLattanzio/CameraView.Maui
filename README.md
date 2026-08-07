# CameraView.Maui

[![CI](https://github.com/MiLattanzio/CameraView.Maui/actions/workflows/ci.yml/badge.svg)](https://github.com/MiLattanzio/CameraView.Maui/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/CameraView.Maui.svg)](https://www.nuget.org/packages/CameraView.Maui)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

CameraView.Maui is a reusable .NET MAUI camera preview control for Android and iOS. It uses Camera2 and AVFoundation and exposes both backward-compatible JPEG frames and high-throughput raw camera buffers.

## Features

- Native live preview on Android and iOS.
- Front and rear camera selection.
- Portrait and landscape output orientation.
- JPEG callbacks plus zero-copy YUV/BGRA frame access.
- Runtime permission requests.
- Automatic camera release and restart across app deactivation, screen lock, and resume.
- Observable camera state and structured cross-platform errors.
- Configurable resolution, pixel format, native frame rate, delivery policy, JPEG quality, throttling, and effective camera capabilities.
- .NET 9 and .NET 10 MAUI targets in the same package.

## Requirements

- .NET 9 or .NET 10 MAUI.
- Android API 28 or later.
- iOS 15.0 or later.

## Quick start

Install the package:

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

Add the control to XAML:

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

iOS applications must also add `NSCameraUsageDescription` to `Info.plist`. The Android camera permission is contributed by the library.

## Receive frames

`OnFrameResult` is raised for every encoded frame produced by the native camera pipeline:

```csharp
private void OnFrameResult(CameraResult result)
{
    if (!result.Success || result.Image is null)
        return;

    byte[] jpeg = result.Image;
    System.Diagnostics.Debug.WriteLine(
        $"JPEG frame: {result.Width}x{result.Height}, " +
        $"sequence {result.SequenceNumber}, {jpeg.Length:N0} bytes");
}
```

## Process raw frames at camera speed

Use the opt-in `Realtime` profile for barcode scanning, OCR, computer vision, or ML. It avoids JPEG encoding and decoding, requests the fastest native frame-rate range, and keeps only the newest frame when processing falls behind:

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Realtime;
CameraPreview.FrameAvailable += OnFrameAvailable;

private void OnFrameAvailable(object? sender, CameraFrameEventArgs args)
{
    CameraFrame frame = args.Frame;
    CameraFramePlane luminance = frame.Planes[0];

    // Consume synchronously without allocating or copying.
    ReadOnlySpan<byte> y = luminance.Span;
    AnalyzeLuminance(
        y,
        luminance.Width,
        luminance.Height,
        luminance.RowStride,
        frame.RotationDegrees);
}
```

`CameraFrameFormat.Native` resolves to `Yuv420` on Android and iOS. Android exposes the native three-plane `YUV_420_888` layout; iOS exposes bi-planar NV12. Always use each plane's `RowStride` and `PixelStride` instead of assuming tightly packed pixels. `Bgra8888` is also available when iOS reports support.

The event frame is borrowed and valid only during that subscriber call. Retain it before asynchronous work and dispose the retained frame promptly:

```csharp
private async void OnFrameAvailable(object? sender, CameraFrameEventArgs args)
{
    using CameraFrame frame = args.Frame.Retain();
    await Task.Run(() => AnalyzeFrame(frame));
}
```

Retaining a raw frame intentionally keeps its native camera buffer alive. `MaxOutstandingFrames` bounds that pressure; when using `Latest`, the camera drops frames rather than building a managed queue.

Configure capture atomically. Assigning one immutable options object produces a single native restart and the same options are reapplied after resume:

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Balanced with
{
    // Presets are available, or request any positive width and height.
    PreferredResolution = new CameraResolution(1600, 1200),
    ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost,
    JpegQuality = 82,
    MaximumFrameRate = 12.5,
    MinimumFrameInterval = TimeSpan.FromMilliseconds(50)
};
```

`Closest`, `AtMost`, and `AtLeast` fall back predictably when the requested size is unavailable; `Exact` reports a structured configuration error instead. When both frame limits are set, the stricter interval wins. `CameraCaptureOptions.Default` preserves the 1.0 platform quality and 720p-or-lower behavior.

Observe what the device actually selected:

```csharp
CameraPreview.EffectiveConfigurationChanged += (_, args) =>
{
    var selected = args.Configuration;
    if (selected is null)
        return;

    Debug.WriteLine(
        $"Capture {selected.CaptureResolution}; " +
        $"preview {selected.PreviewResolution}; " +
        $"fallback {selected.UsedResolutionFallback}");
};
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

Important frame characteristics:

- `OnFrameResult` remains the compatibility path and contains a complete JPEG byte array.
- `FrameAvailable` emits JPEG, YUV, or BGRA according to `CaptureOptions.FrameFormat`.
- The callback runs on a native capture queue and is not guaranteed to be the UI thread.
- A new frame can arrive before processing of the previous frame has completed.
- Exceptions from individual subscribers are isolated and written to debug output; still handle processing errors locally so failed work is visible to the application.

## Process frames without building a backlog

Camera frames can arrive much faster than OCR, barcode, ML, upload, or disk processing can finish. Do not create an unbounded task for every callback. A bounded channel with capacity one keeps only the most recent frame while a single consumer is busy:

```csharp
using System.Threading.Channels;

public partial class CameraPage : ContentPage
{
    private Channel<byte[]>? _frames;
    private CancellationTokenSource? _frameProcessingCts;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var frames = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        var cts = new CancellationTokenSource();

        Volatile.Write(ref _frames, frames);
        _frameProcessingCts = cts;
        CameraPreview.OnFrameResult += OnFrameResult;
        CameraPreview.Enabled = true;

        _ = ConsumeFramesAsync(frames.Reader, cts);
    }

    protected override void OnDisappearing()
    {
        CameraPreview.Enabled = false;
        CameraPreview.OnFrameResult -= OnFrameResult;

        Interlocked.Exchange(ref _frames, null)?.Writer.TryComplete();
        Interlocked.Exchange(ref _frameProcessingCts, null)?.Cancel();

        base.OnDisappearing();
    }

    private void OnFrameResult(CameraResult result)
    {
        if (result.Success && result.Image is not null)
            Volatile.Read(ref _frames)?.Writer.TryWrite(result.Image);
    }

    private static async Task ConsumeFramesAsync(
        ChannelReader<byte[]> frames,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await foreach (var jpeg in frames.ReadAllAsync(cancellationToken))
                await ProcessJpegAsync(jpeg, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page is no longer visible.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static Task ProcessJpegAsync(
        byte[] jpeg,
        CancellationToken cancellationToken)
    {
        // Pass jpeg, or a MemoryStream over jpeg, to an OCR, barcode,
        // machine-learning, or upload service here.
        return Task.CompletedTask;
    }
}
```

For a processor that accepts a `Stream`, avoid another byte-array allocation:

```csharp
private static async Task AnalyzeFrameAsync(
    byte[] jpeg,
    Func<Stream, CancellationToken, Task> analyzeAsync,
    CancellationToken cancellationToken)
{
    using var stream = new MemoryStream(jpeg, writable: false);
    await analyzeAsync(stream, cancellationToken);
}
```

Pass the asynchronous method exposed by the OCR, barcode, ML, or image-decoding API as `analyzeAsync`.

## Save one frame as a JPEG

The frame is already encoded, so saving it does not require an image library:

```csharp
private int _saveNextFrame;

private void OnSaveFrameClicked(object sender, EventArgs e) =>
    Interlocked.Exchange(ref _saveNextFrame, 1);

private async void OnFrameResult(CameraResult result)
{
    if (!result.Success ||
        result.Image is null ||
        Interlocked.Exchange(ref _saveNextFrame, 0) == 0)
        return;

    try
    {
        var path = Path.Combine(FileSystem.CacheDirectory, "camera-frame.jpg");
        await File.WriteAllBytesAsync(path, result.Image);
        System.Diagnostics.Debug.WriteLine($"Frame saved to {path}");
    }
    catch (Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(
            $"Unable to save frame: {exception}");
    }
}
```

Use `FileSystem.CacheDirectory` for temporary files. For a user-visible photo, copy the file to an application-appropriate location and request any platform storage permissions required by that location.

## Display an encoded frame in a MAUI Image

Use this for an occasional snapshot or processed result, not for every live-preview frame:

```csharp
private static Task ShowFrameAsync(Image target, byte[] jpeg) =>
    MainThread.InvokeOnMainThreadAsync(() =>
    {
        target.Source = ImageSource.FromStream(
            () => new MemoryStream(jpeg, writable: false));
    });
```

The camera control already renders the live native preview. Continuously assigning every frame to another `Image` decodes the same JPEG again and adds unnecessary CPU, memory, and garbage-collection pressure.

## Decode or analyze pixels

Prefer `CameraCaptureOptions.Realtime` when an algorithm accepts luminance or YUV planes. This removes both JPEG encoding in the camera pipeline and JPEG decoding in the application. Use JPEG only when the downstream API explicitly needs encoded image data.

1. Prefer synchronous access to the borrowed `CameraFrame` for the lowest latency.
2. Call `Retain()` before asynchronous processing and always dispose the returned frame.
3. Respect plane dimensions, row stride, pixel stride, `RotationDegrees`, and `IsMirrored`.
4. Reuse model, tensor, and conversion resources.
5. Marshal only final UI state back through `MainThread`.

Avoid `System.Drawing.Common` in Android/iOS application code; choose a decoder explicitly designed for the target platforms.

## Control the camera

```csharp
// Switch camera. The native session is reconfigured automatically.
CameraPreview.Camera = CameraOptions.Front;

// Change encoded-frame orientation.
CameraPreview.Orientation = CameraOrientation.Landscape;

// Release or restart the camera explicitly.
CameraPreview.Enabled = false;
CameraPreview.Enabled = true;
```

The control also releases the native camera when the MAUI window is deactivated and restores it after activation if `Enabled` remains `true`.

## Observe camera state and errors

`State` and `IsRunning` describe the actual native session, rather than only the requested value of `Enabled`:

```csharp
CameraPreview.StateChanged += (_, args) =>
{
    // StateChanged is dispatched through the MAUI dispatcher.
    StateLabel.Text = $"{args.State} ({args.Camera})";
};

CameraPreview.ErrorOccurred += (_, args) =>
{
    ErrorLabel.Text = $"{args.Code}: {args.Message}";

    System.Diagnostics.Debug.WriteLine(
        $"Native code: {args.PlatformCode}; " +
        $"recoverable: {args.IsRecoverable}; {args.Exception}");
};
```

Possible states are `Stopped`, `Starting`, `Running`, `Suspended`, `PermissionDenied`, and `Failed`. Stable error codes distinguish permission denial, unavailable or busy cameras, session configuration failures, device disconnection, and frame capture failures.

`StateChanged` and `ErrorOccurred` run through the view dispatcher. `OnFrameResult` and `FrameAvailable` continue to run on the native capture queue for throughput.

## API summary

| Member | Default | Description |
| --- | --- | --- |
| `Camera` | `CameraOptions.Rear` | Selects the rear or front camera. |
| `Orientation` | `CameraOrientation.Landscape` | Controls portrait or landscape output. |
| `Enabled` | `true` | Controls native camera ownership and capture. |
| `CaptureOptions` | `CameraCaptureOptions.Default` | Atomically configures resolution, frame format, native rate, delivery policy, JPEG quality, and throttling. |
| `EffectiveConfiguration` | `null` | Reports selected sizes, format, native rate, delivery settings, and device capabilities. |
| `State` | `CameraState.Stopped` | Reports the current native camera lifecycle state. |
| `IsRunning` | `false` | Indicates that native capture is actually running. |
| `OnFrameResult` | — | Emits successful JPEG frames through `CameraResult.Image`. |
| `FrameAvailable` | — | Emits borrowed JPEG, YUV, or BGRA `CameraFrame` values without forcing an encoded copy. |
| `StateChanged` | — | Reports state transitions on the MAUI dispatcher. |
| `ErrorOccurred` | — | Reports structured camera failures on the MAUI dispatcher. |
| `EffectiveConfigurationChanged` | — | Reports configuration negotiation and clearing on the MAUI dispatcher. |

## Frame-processing recommendations

- Drop or throttle frames instead of queueing an unlimited number.
- Keep native callback work short.
- Dispose every retained raw frame; never retain more frames than processing can finish.
- Do not update MAUI controls outside `MainThread`.
- Avoid Base64 conversion unless an external protocol requires it; it increases payload size and allocations.
- Avoid saving every frame to flash storage.
- Cancel processing when the page disappears.
- Test memory use and sustained processing on physical Android and iOS devices.

## Documentation

- [Getting started](docs/getting-started.md)
- [Platform setup](docs/platform-setup.md)
- [API reference](docs/api-reference.md)
- [Architecture and lifecycle](docs/architecture.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Roadmap](docs/ROADMAP.md)
- [Changelog](docs/CHANGELOG.md)
- [Contributing](docs/CONTRIBUTING.md)
- [Release and Trusted Publishing](docs/releasing.md)

## Build

```shell
dotnet workload restore CameraView.Maui.sln
dotnet restore CameraView.Maui.sln
dotnet build CameraView.Maui.sln
```

## License

CameraView.Maui is licensed under the [MIT License](LICENSE).
