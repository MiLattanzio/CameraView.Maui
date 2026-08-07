# API reference

All public types are in the `CameraView.Maui` namespace.

## CameraView

A MAUI `View` that displays the native camera preview and emits JPEG frames.

### Properties

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Camera` | `CameraOptions` | `Rear` | Selects the front or rear camera. Changing it restarts capture. |
| `Orientation` | `CameraOrientation` | `Landscape` | Selects portrait or landscape frame orientation. Changing it restarts capture. |
| `Enabled` | `bool` | `true` | Controls whether the camera session should be active. |
| `State` | `CameraState` | `Stopped` | Read-only current lifecycle state of the native camera session. |
| `IsRunning` | `bool` | `false` | Read-only convenience value equivalent to `State == CameraState.Running`. |
| `CaptureOptions` | `CameraCaptureOptions` | `Default` | Immutable requested resolution, selection policy, JPEG quality, and delivery-rate limits. Replacing it restarts capture once. |
| `EffectiveConfiguration` | `CameraCaptureConfiguration` | `null` | Read-only capture and preview configuration actually selected after startup. |

### Events

`OnFrameResult` receives a `CameraResult` for each encoded frame.

```csharp
CameraPreview.OnFrameResult += result =>
{
    if (result.Success)
    {
        byte[] jpeg = result.Image;
    }
};
```

The event is raised on the platform capture queue, not necessarily the UI thread.

`StateChanged` reports transitions together with the previous state and selected camera. `ErrorOccurred` reports a structured `CameraErrorEventArgs`. Both events are dispatched through the MAUI dispatcher and may update UI controls directly.

```csharp
CameraPreview.StateChanged += (_, args) =>
    StateLabel.Text = args.State.ToString();

CameraPreview.ErrorOccurred += (_, args) =>
    ErrorLabel.Text = $"{args.Code}: {args.Message}";
```

`EffectiveConfigurationChanged` is also dispatched through the MAUI dispatcher. Its `Configuration` is `null` while a new native session is being negotiated, then contains the selected output once startup succeeds.

Exceptions thrown by a frame, state, or error subscriber are caught and written to the debug output so one consumer cannot terminate native capture or prevent other subscribers from running.

### Methods

- `SetResult(byte[] image)` emits a successful result when the array is non-empty. It is primarily used by the native handler.
- `Cancel()` emits an unsuccessful result.

### Compatibility fields

`CameraPreview` and `CameraEnable` are bindable-property aliases retained for compatibility with the earlier Xamarin control. New code should use `OrientationProperty` and `EnabledProperty`.

## CameraResult

| Member | Type | Description |
| --- | --- | --- |
| `Success` | `bool` | Indicates whether an image is available. |
| `Image` | `byte[]` | JPEG bytes for a successful result; no image is assigned to a cancelled result. |
| `Width`, `Height` | `int` | Encoded frame dimensions reported by the native pipeline. |
| `Timestamp` | `DateTimeOffset` | UTC timestamp captured when the frame is delivered. |
| `Orientation` | `CameraOrientation` | Orientation requested for the encoded frame. |
| `Camera` | `CameraOptions` | Camera that produced the frame. |
| `SequenceNumber` | `long` | Monotonically increasing number for successful results from this control instance. |
| `Configuration` | `CameraCaptureConfiguration` | Configuration snapshot associated with the frame when available. |

The public `CameraResult(byte[])` constructor from 1.0 remains unchanged. Native metadata is populated only for frames produced by `CameraView`.

## CameraCaptureOptions

`CameraCaptureOptions` is an immutable record. Assign a complete instance to `CameraView.CaptureOptions`; use a `with` expression for small changes without mutating an object already used by a running session.

| Member | Default | Description |
| --- | --- | --- |
| `PreferredResolution` | `CameraResolution.Default` | Preferred encoded size. Supports presets and arbitrary positive dimensions. |
| `ResolutionSelectionMode` | `Closest` | Controls fallback when the exact size is unavailable. |
| `JpegQuality` | `null` | Quality from 1 to 100. `null` preserves the platform behavior used by 1.0. |
| `MaximumFrameRate` | `0` | Maximum delivered frames per second as a `double`; zero disables this constraint. |
| `MinimumFrameInterval` | `TimeSpan.Zero` | Minimum elapsed time between delivered frames. |

When both rate constraints are supplied, the stricter (longer) interval is used. Throttling occurs before managed delivery, uses a monotonic clock, and never creates a queue.

Reusable profiles are `Default`, `LowBandwidth`, `Balanced`, and `HighQuality`.

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Balanced with
{
    PreferredResolution = new CameraResolution(1440, 1080),
    ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost,
    MaximumFrameRate = 12.5
};
```

## CameraCaptureConfiguration

Reports the effective native output after negotiation.

| Member | Description |
| --- | --- |
| `RequestedOptions` | Immutable options that produced this session. |
| `CaptureResolution` | Actual encoded-frame size. |
| `PreviewResolution` | Actual native preview buffer size. |
| `JpegQuality` | Effective configured quality; `null` means the Android platform default remains in use. |
| `MinimumFrameInterval` | Active minimum interval enforced before managed delivery. |
| `MaximumFrameRate` | Rate derived from the active interval, or zero when unlimited. |
| `UsedResolutionFallback` | Whether the selected capture dimensions differ from the requested dimensions. |

## CameraResolution

`CameraResolution` is an immutable width/height value. `Default` keeps the 1.0 720p-or-lower selection. Presets are `Qvga`, `Vga`, `Hd720p`, `Hd1080p`, and `Uhd4K`; arbitrary sizes use `new CameraResolution(width, height)`.

`HasSameDimensions` compares sizes independently of portrait/landscape ordering. `PixelCount` and `IsDefault` are available for diagnostics.

## CameraResolutionSelectionMode

- `Closest`: minimizes aspect-ratio and pixel-count differences.
- `AtMost`: chooses the closest compatible size no greater than the request; falls back to `Closest` if none exists.
- `AtLeast`: chooses the closest compatible size no smaller than the request; falls back to `Closest` if none exists.
- `Exact`: does not fall back and reports `SessionConfigurationFailed` when unavailable.

`new CameraResult()` creates an unsuccessful result. `new CameraResult(byte[])` creates a successful result and rejects a null array.

## CameraOptions

- `Rear`
- `Front`

## CameraOrientation

- `Portrait`
- `Landscape`

## CameraState

| Value | Meaning |
| --- | --- |
| `Stopped` | Capture is explicitly disabled or the handler is disconnected. |
| `Starting` | Permission and native session startup are in progress. |
| `Running` | The native capture session has been configured and started. |
| `Suspended` | The view/window is inactive or the native session is temporarily interrupted. |
| `PermissionDenied` | The operating system did not grant camera access. |
| `Failed` | Native startup or capture failed. See `ErrorOccurred`. |

## CameraErrorCode

- `Unknown`
- `PermissionDenied`
- `CameraUnavailable`
- `CameraInUse`
- `SessionConfigurationFailed`
- `DeviceDisconnected`
- `CaptureFailed`

These values are platform-independent and stable. Platform-specific diagnostics are available separately through `CameraErrorEventArgs.PlatformCode`.

## CameraStateChangedEventArgs

| Member | Type | Description |
| --- | --- | --- |
| `PreviousState` | `CameraState` | State before the transition. |
| `State` | `CameraState` | New camera state. |
| `Camera` | `CameraOptions` | Camera selected when the transition was emitted. |

## CameraCaptureConfigurationChangedEventArgs

| Member | Type | Description |
| --- | --- | --- |
| `PreviousConfiguration` | `CameraCaptureConfiguration` | Previously effective configuration, or `null`. |
| `Configuration` | `CameraCaptureConfiguration` | Newly effective configuration, or `null` while restarting/stopped/failed. |

## CameraErrorEventArgs

| Member | Type | Description |
| --- | --- | --- |
| `Code` | `CameraErrorCode` | Stable cross-platform category. |
| `Message` | `string` | User-diagnostic description. |
| `Camera` | `CameraOptions` | Camera selected when the error occurred. |
| `IsRecoverable` | `bool` | Whether retrying after correcting the condition can reasonably succeed. |
| `PlatformCode` | `string` | Native code or condition name when available; otherwise `null`. |
| `Exception` | `Exception` | Managed/native exception when available; otherwise `null`. |

`IsRecoverable` does not mean the library retries continuously. Applications can retry by correcting the reported condition and toggling `Enabled`, changing `Camera`, or allowing the normal activation lifecycle to restart capture.

## CameraViewAppBuilderExtensions

`UseCameraView(MauiAppBuilder)` registers `CameraViewHandler` with the MAUI handler collection and returns the same builder.
