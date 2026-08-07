# Architecture and lifecycle

## Control and handler

`CameraView` is the cross-platform MAUI view. Its bindable properties are mapped by `CameraViewHandler` to a platform-specific `NativeCameraView`:

- Android uses Camera2, `TextureView`, and `ImageReader`.
- iOS uses `AVCaptureSession`, `AVCaptureVideoPreviewLayer`, and `AVCaptureVideoDataOutput`.

Changing camera, orientation, enabled state, or the immutable `CaptureOptions` value increments a configuration version. Asynchronous permission and camera-start work verifies this version before touching the current native view. Stale operations and stale configuration callbacks therefore cannot restart a camera or replace effective settings after a newer configuration has been applied.

## Lifecycle

The handler subscribes to the owning MAUI `Window` while the view is loaded.

- `Deactivated` invalidates pending work and stops the native session.
- `Activated` reapplies the latest configuration.
- `Unloaded` stops capture and detaches window events.
- Handler disconnection performs the same cleanup.

This keeps camera ownership aligned with application visibility and recovers from native session loss after screen lock.

## State and error pipeline

The handler is the authority for observable state. Each configuration receives a monotonically increasing version; native started, interrupted, and failed callbacks carry that version back to the handler. A callback is discarded if the view, handler, configuration, or window activation has changed in the meantime.

Normal transitions are:

```text
Stopped -> Starting -> Running
Running -> Suspended -> Starting -> Running
Starting/Running -> PermissionDenied
Starting/Running -> Failed
Any state -> Stopped when Enabled becomes false
```

Android reports `Running` only after Camera2 accepts the repeating capture request. Camera-device errors, disconnection, camera contention, and capture-session configuration failures are mapped to stable `CameraErrorCode` values.

iOS reports `Running` after `AVCaptureSession.StartRunning` succeeds. AVFoundation runtime errors and interruption notifications move the observable state to `Failed` or `Suspended`; interruption recovery reports `Running` only after the native session is running again.

State and error events are marshalled through the view dispatcher. JPEG frames remain on the native capture queue for throughput.

## Frame pipeline

Android requests a JPEG output from `ImageReader`. iOS converts captured pixel buffers to JPEG through Core Image and UIKit. Both implementations invoke the managed callback from their capture queue.

Capture-size negotiation uses the native sizes exposed by the selected device. `Closest` scores both aspect-ratio and pixel-count differences; `AtMost` and `AtLeast` first constrain dimensions, while `Exact` rejects an unsupported request. Android independently chooses a preview buffer closest to the negotiated capture aspect ratio. iOS selects an `AVCaptureDeviceFormat` and uses input-priority session configuration, so the reported resolution comes from the active device format rather than a presumed preset.

Delivery throttling uses a monotonic clock. Android acquires and closes the latest `ImageReader` image even when dropping it, preventing producer backpressure. iOS relies on `AlwaysDiscardsLateVideoFrames` and drops samples before JPEG encoding. Neither platform creates a managed frame backlog.

Consumers should avoid expensive synchronous work in `OnFrameResult`. Copy or enqueue the byte array when additional processing is required, and marshal only UI updates to the main thread.
