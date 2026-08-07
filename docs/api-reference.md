# API reference

All public types are in the `CameraView.Maui` namespace.

## CameraView

A MAUI `View` that displays the native camera preview and emits JPEG or raw native frames.

### Properties

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Camera` | `CameraOptions` | `Rear` | Selects the front or rear camera. Changing it restarts capture. |
| `Orientation` | `CameraOrientation` | `Landscape` | Selects portrait or landscape frame orientation. Changing it restarts capture. |
| `Enabled` | `bool` | `true` | Controls whether the camera session should be active. |
| `State` | `CameraState` | `Stopped` | Read-only current lifecycle state of the native camera session. |
| `IsRunning` | `bool` | `false` | Read-only convenience value equivalent to `State == CameraState.Running`. |
| `CaptureOptions` | `CameraCaptureOptions` | `Default` | Immutable requested resolution, format, native frame rate, delivery policy, JPEG quality, and delivery-rate limits. Replacing it restarts capture once. |
| `EffectiveConfiguration` | `CameraCaptureConfiguration` | `null` | Read-only capture and preview configuration actually selected after startup. |
| `ControlOptions` | `CameraControlOptions` | `Default` | Immutable requested zoom, torch, focus, exposure, and preview-mirroring controls. Replacing it updates the active native session without restarting capture. |
| `EffectiveControls` | `CameraControlState` | `null` | Read-only applied controls and capabilities of the selected camera. |

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

`FrameAvailable` receives every configured output format. The event is raised on the platform capture queue, not necessarily the UI thread.

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Realtime;
CameraPreview.FrameAvailable += (_, args) =>
{
    CameraFrame frame = args.Frame;
    ReadOnlySpan<byte> luminance = frame.Planes[0].Span;
};
```

The event's `CameraFrame` is borrowed and disposed immediately after that subscriber returns. Use `frame.Retain()` to create an independently disposable frame before returning or awaiting. One subscriber disposing its event frame does not affect other subscribers.

`StateChanged` reports transitions together with the previous state and selected camera. `ErrorOccurred` reports a structured `CameraErrorEventArgs`. Both events are dispatched through the MAUI dispatcher and may update UI controls directly.

```csharp
CameraPreview.StateChanged += (_, args) =>
    StateLabel.Text = args.State.ToString();

CameraPreview.ErrorOccurred += (_, args) =>
    ErrorLabel.Text = $"{args.Code}: {args.Message}";
```

`EffectiveConfigurationChanged` is also dispatched through the MAUI dispatcher. Its `Configuration` is `null` while a new native session is being negotiated, then contains the selected output once startup succeeds.

`EffectiveControlsChanged` is dispatched through the MAUI dispatcher after controls are applied. Unsupported or out-of-range requests do not stop capture: inspect the effective value and fallback flags. The state is cleared while a new camera is starting, then repopulated with the new camera's capabilities.

Exceptions thrown by a frame, state, or error subscriber are caught and written to the debug output so one consumer cannot terminate native capture or prevent other subscribers from running.

### Methods

- `SetResult(byte[] image)` emits a successful JPEG result and frame when the array is non-empty. It is primarily used by the native handler.
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

## CameraFrame

An immutable view over one encoded or native camera buffer. It implements `IDisposable` because raw frames can own Android `Image` or iOS `CVPixelBuffer` resources.

| Member | Description |
| --- | --- |
| `Format` | Effective `Jpeg`, `Yuv420`, or `Bgra8888` format. A delivered frame is never reported as the `Native` request alias. |
| `Width`, `Height` | Native output dimensions. |
| `Timestamp` | UTC delivery timestamp. |
| `Orientation`, `Camera` | Requested orientation and selected camera. |
| `SequenceNumber` | Monotonically increasing number shared with the JPEG compatibility result. |
| `Configuration` | Effective configuration associated with the frame. |
| `RotationDegrees` | Clockwise rotation required before displaying or analyzing an unrotated raw buffer. |
| `IsMirrored` | Whether the delivered pixels are already mirrored. |
| `Planes` | Read-only list of `CameraFramePlane` values. |
| `IsDisposed` | Whether this particular frame lease has been released. |
| `Retain()` | Creates a new frame lease over the same buffer for asynchronous or deferred work. |

The original event frame must not be cached. Dispose every retained frame promptly; retaining `MaxOutstandingFrames` native buffers can temporarily prevent the camera from delivering another raw frame.

## CameraFramePlane

`Span` exposes zero-copy read-only bytes while the owning frame is alive. `Length`, `Width`, `Height`, `RowStride`, and `PixelStride` describe the native layout. `CopyTo` and `ToArray` are explicit copying helpers.

For `Yuv420`, plane zero is luminance. Android normally supplies separate Y, U, and V planes; iOS normally supplies Y plus an interleaved UV plane. Consumers must inspect the plane count and strides rather than assuming one layout. A JPEG frame has one encoded plane with zero pixel/row stride.

## CameraCaptureOptions

`CameraCaptureOptions` is an immutable record. Assign a complete instance to `CameraView.CaptureOptions`; use a `with` expression for small changes without mutating an object already used by a running session.

| Member | Default | Description |
| --- | --- | --- |
| `PreferredResolution` | `CameraResolution.Default` | Preferred encoded size. Supports presets and arbitrary positive dimensions. |
| `ResolutionSelectionMode` | `Closest` | Controls fallback when the exact size is unavailable. |
| `JpegQuality` | `null` | Quality from 1 to 100. `null` preserves the platform behavior used by 1.0. |
| `FrameFormat` | `Jpeg` | `Jpeg`, `Native`, `Yuv420`, or `Bgra8888`. `Native` negotiates the fastest raw format. |
| `FrameDeliveryMode` | `Latest` | Drops stale native frames or requests sequential delivery. |
| `MaxOutstandingFrames` | `2` | Native buffer capacity from 2 through 8. Higher values permit more retained frames but consume more memory. |
| `FrameRateMode` | `PlatformDefault` | Keeps the native default, selects the maximum supported range, or chooses the range closest to `TargetFrameRate`. |
| `TargetFrameRate` | `0` | Positive FPS required when `FrameRateMode` is `Closest`. |
| `MaximumFrameRate` | `0` | Maximum delivered frames per second as a `double`; zero disables this constraint. |
| `MinimumFrameInterval` | `TimeSpan.Zero` | Minimum elapsed time between delivered frames. |

When both rate constraints are supplied, the stricter (longer) interval is used. Throttling occurs before managed delivery, uses a monotonic clock, and never creates a queue.

Reusable profiles are `Default`, `LowBandwidth`, `Balanced`, `HighQuality`, and `Realtime`. `Realtime` requests native YUV, the maximum supported native frame-rate range, latest-frame delivery, 720p, and three outstanding buffers.

```csharp
CameraPreview.CaptureOptions = CameraCaptureOptions.Balanced with
{
    PreferredResolution = new CameraResolution(1440, 1080),
    ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost,
    MaximumFrameRate = 12.5
};
```

## CameraControlOptions

`CameraControlOptions` is an immutable record. Replace `CameraView.ControlOptions` with a complete value or a `with` expression. Unlike `CaptureOptions`, control changes are applied to the running Camera2 request or AVFoundation device without rebuilding the capture session.

| Member | Default | Description |
| --- | --- | --- |
| `ZoomFactor` | `1` | Positive optical/digital zoom factor. The effective value is clamped to the selected camera's range. Values below 1 are accepted for devices that expose an ultra-wide zoom ratio. |
| `TorchEnabled` | `false` | Requests continuous torch illumination. It falls back to `false` when the selected camera has no supported torch. |
| `FocusMode` | `Continuous` | Requests continuous autofocus or a one-shot `Single` autofocus operation. |
| `FocusPoint` | `null` | Optional `CameraPoint` normalized from `(0,0)` at the preview's top-left to `(1,1)` at its bottom-right. `null` restores the centered/default metering point. |
| `ExposureCompensation` | `0` | Exposure bias in EV. It is clamped and, where required, quantized to the native step. |
| `PreviewMirroring` | `Automatic` | Mirrors the front preview and leaves the rear preview unmirrored, or explicitly forces either behavior. It does not change frame buffers. |

```csharp
CameraPreview.ControlOptions = CameraPreview.ControlOptions with
{
    ZoomFactor = 2,
    TorchEnabled = true,
    FocusMode = CameraFocusMode.Single,
    FocusPoint = new CameraPoint(0.35, 0.6),
    ExposureCompensation = 0.5,
    PreviewMirroring = CameraPreviewMirroringMode.Automatic
};
```

Invalid universal values such as a non-positive/non-finite zoom or non-finite exposure are rejected immediately. Hardware-specific limits use deterministic fallback instead, allowing one options value to survive front/rear switching and lifecycle resume.

## CameraControlState and capabilities

`EffectiveControls` reports the active `ZoomFactor`, `TorchEnabled`, nullable `FocusMode`/`FocusPoint`, `ExposureCompensation`, and `IsPreviewMirrored`. `RequestedOptions` preserves the complete request. `UsedZoomFallback`, `UsedTorchFallback`, `UsedFocusFallback`, and `UsedExposureFallback` identify values the hardware could not apply exactly.

`CameraControlCapabilities` exposes:

- `MinimumZoomFactor`, `MaximumZoomFactor`, and `IsZoomSupported`;
- `IsTorchSupported`;
- `IsFocusPointSupported`, `SupportedFocusModes`, and `SupportsFocusMode`;
- `MinimumExposureCompensation`, `MaximumExposureCompensation`, `ExposureCompensationStep`, and `SupportsExposureCompensation`.

Capability values belong to the currently selected camera and can change after switching between front and rear cameras.

## CameraPoint

An immutable normalized preview coordinate. Both values must be finite and between zero and one. `CameraPoint.Center` is `(0.5, 0.5)`. The native implementation maps the point through the current rotation and preview mirroring before configuring focus and, when available, exposure metering.

## CameraFocusMode

- `Continuous`: keeps native continuous autofocus active, optionally around `FocusPoint`.
- `Single`: triggers one autofocus operation at `FocusPoint`, or at the platform-default region when the point is `null`.

## CameraPreviewMirroringMode

- `Automatic`: mirrors only the front-camera preview.
- `Mirrored`: always mirrors the preview.
- `Unmirrored`: never mirrors the preview.

Preview mirroring is independent from delivered data. Use `CameraFrame.IsMirrored` to determine the actual frame-buffer behavior.

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
| `FrameFormat` | Concrete delivered format after resolving `Native`. |
| `FrameDeliveryMode` | Effective latest or sequential native delivery policy. |
| `NativeFrameRate` | Requested native range accepted by the platform, or `null` for the platform default. |
| `Capabilities` | Supported concrete formats, capture resolutions, and native frame-rate ranges. |
| `UsedResolutionFallback` | Whether the selected capture dimensions differ from the requested dimensions. |
| `UsedFrameFormatFallback` | Whether a concrete requested format differs from the delivered format. `Native` resolution is not considered fallback. |

`MaximumFrameRate` throttles managed delivery. `NativeFrameRate` configures camera production. They are independent: native-rate selection can reduce sensor/ISP work, while delivery throttling can intentionally skip additional frames before managed callbacks.

## CameraCaptureCapabilities

`FrameFormats`, `CaptureResolutions`, and `FrameRateRanges` are immutable device selections reported for the active camera. `MaximumFrameRate`, `SupportsFrameFormat`, and `SupportsCaptureResolution` provide common queries. `Native` is supported when at least one raw format is available.

## CameraFrameFormat

- `Jpeg`: encoded bytes and the backward-compatible `OnFrameResult` event.
- `Native`: request alias for the fastest platform raw format; delivered frames report the concrete format.
- `Yuv420`: three-plane `YUV_420_888` on Android or bi-planar NV12 on iOS.
- `Bgra8888`: one packed BGRA plane; currently available only when iOS reports support.

## CameraFrameDeliveryMode

- `Latest`: discard stale native frames and prioritize low latency.
- `Sequential`: request frames in producer order. Slow or retained frames can apply native backpressure.

## CameraFrameRateMode

- `PlatformDefault`: do not override the device's native frame-rate choice.
- `Maximum`: select the supported range with the highest upper bound.
- `Closest`: select the supported range nearest to `TargetFrameRate`.

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
- `ControlConfigurationFailed`

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

## CameraControlStateChangedEventArgs

| Member | Type | Description |
| --- | --- | --- |
| `PreviousState` | `CameraControlState` | Previously effective controls, or `null`. |
| `State` | `CameraControlState` | Newly effective controls, or `null` while restarting/stopped/failed. |

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
