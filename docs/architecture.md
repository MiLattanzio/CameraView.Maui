# Architecture and lifecycle

## Control and handler

`CameraView` is the cross-platform MAUI view. Its bindable properties are mapped by `CameraViewHandler` to a platform-specific `NativeCameraView`:

- Android uses Camera2, `SurfaceView`, and `ImageReader`.
- iOS uses `AVCaptureSession`, `AVCaptureVideoPreviewLayer`, and `AVCaptureVideoDataOutput`.

Changing camera, orientation, enabled state, or the immutable `CaptureOptions` value increments a configuration version. Asynchronous permission and camera-start work verifies this version before touching the current native view. Stale operations and stale configuration callbacks therefore cannot restart a camera or replace effective settings after a newer configuration has been applied.

`ControlOptions` has an independent version. Replacing it does not invalidate or restart capture: the handler forwards it to the current native view, and only the newest control callback may replace `EffectiveControls`. A full camera reconfiguration invalidates both versions and applies the latest control options to the new session.

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

State and error events are marshalled through the view dispatcher. JPEG and raw frames remain on the native capture queue for throughput.

## Frame pipeline

The default remains the compatible JPEG pipeline. Android requests JPEG from `ImageReader`; iOS converts a BGRA pixel buffer through Core Image and UIKit. `OnFrameResult` receives the same managed `byte[]` contract as versions 1.0 through 1.2.1.

The opt-in raw pipeline avoids encoding and decoding. Android requests `YUV_420_888` and exposes direct addresses for the native Y, U, and V planes. iOS requests 8-bit bi-planar YUV (NV12), or BGRA when explicitly selected, retains the `CMSampleBuffer`, locks the `CVPixelBuffer` for read access, and exposes its native planes. `CameraFramePlane.Span` therefore performs no per-frame copy and retained leases remain valid after the AVFoundation delegate returns.

Each native buffer begins with one internal reference. `CameraView` creates a separate short-lived `CameraFrame` lease for every `FrameAvailable` subscriber, then releases it when that subscriber returns. `Retain()` increments the buffer reference and returns an independently disposable frame for asynchronous work. The Android `Image` or iOS `CVPixelBuffer` is closed/unlocked only when the final lease is disposed; a finalizer is a safety net, not a processing strategy.

Capture-size negotiation uses the native sizes exposed by the selected device. `Closest` scores both aspect-ratio and pixel-count differences; `AtMost` and `AtLeast` first constrain dimensions, while `Exact` rejects an unsupported request. Android independently chooses a stable 720p-or-closest preview buffer, so changing processing resolution or frame format does not reshape the viewfinder. Its `SurfaceView` is centred at the oriented source aspect ratio, enlarged by one uniform fill factor, and clipped by its parent only along the overflowing dimension. iOS selects an `AVCaptureDeviceFormat` and uses input-priority session configuration, so the reported resolution comes from the active device format rather than a presumed preset.

Native frame-rate negotiation and managed delivery throttling are independent. `FrameRateMode` selects a Camera2 AE target range or exact AVFoundation frame duration. Delivery throttling uses a monotonic clock after native production. Android acquires and closes the latest `ImageReader` image even when dropping it; iOS drops samples before JPEG encoding or raw-frame leasing.

`FrameDeliveryMode.Latest` uses `AcquireLatestImage` and `AlwaysDiscardsLateVideoFrames`, prioritizing latency without a managed backlog. `Sequential` requests producer order, while `MaxOutstandingFrames` limits retained Android buffers. Holding every native buffer may temporarily stall delivery by design rather than allowing unbounded allocation.

Consumers should avoid expensive synchronous work in either frame callback. JPEG byte arrays are independently owned and can be queued into a bounded channel. Raw frames should normally be consumed synchronously; asynchronous consumers call `Retain()` and must dispose the lease. Only UI updates are marshalled to the main thread.

## Interactive controls

The cross-platform control contract separates requested values from applied state. `CameraControlNegotiator` validates device-independent values, clamps zoom and exposure to reported ranges, quantizes exposure when Android reports a step, falls back to supported focus modes, disables unsupported torch requests, and resolves automatic preview mirroring from the selected camera. Applications observe the result through `EffectiveControls` rather than guessing native behavior.

Android applies controls to the existing Camera2 repeating request. API 30 and later use `CONTROL_ZOOM_RATIO`; older devices use a centered `SCALER_CROP_REGION`. Focus points are normalized in preview coordinates, mapped through aspect-fill cropping, unmirrored/rotated into sensor coordinates, and converted to bounded AF/AE metering rectangles. Single focus submits one `CONTROL_AF_TRIGGER_START` request before returning the repeating request to idle. Torch and exposure compensation share the same atomic request update.

iOS locks the active `AVCaptureDevice` only while assigning zoom, torch, focus point/mode, and exposure target bias. `AVCaptureVideoPreviewLayer` converts normalized visible-preview points to device points of interest. Preview mirroring is updated on the preview connection alone; the video-data connection retains the established front-camera output behavior, which remains visible through `CameraFrame.IsMirrored`.

Control application failures raise `ControlConfigurationFailed` without tearing down an otherwise healthy capture session. Unsupported values are normal negotiation outcomes, not errors. Requested options survive rotation, camera switching, window deactivation, and resume; native capabilities and effective values are recomputed for every new selected camera.
