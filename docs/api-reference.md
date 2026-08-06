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
| `Resolution` | `CameraResolution` | `Default` | Preferred encoded-frame resolution preset. Unsupported requests use the closest available native size. |
| `JpegQuality` | `int` | `85` | JPEG quality from 1 to 100. The default preserves the previous behavior. |
| `MaximumFrameRate` | `int` | `0` | Maximum delivered frame rate; zero means no explicit limit. |
| `MinimumFrameInterval` | `TimeSpan` | `Zero` | Minimum time between delivered frames. `MaximumFrameRate` takes precedence when both are set. |
| `EffectiveConfiguration` | `CameraCaptureConfiguration` | `null` | Read-only configuration selected by the native camera after startup. |

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

## CameraCaptureConfiguration

Reports the effective native output after negotiation. `Width` and `Height` are the selected encoded dimensions; `JpegQuality`, `MaximumFrameRate`, and `MinimumFrameInterval` report the active requested limits.

## CameraResolution

- `Default` (the 1.0 720p-or-lower behavior)
- `Qvga`
- `Vga`
- `Hd720p`
- `Hd1080p`

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
