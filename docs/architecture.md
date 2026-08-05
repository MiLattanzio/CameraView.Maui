# Architecture and lifecycle

## Control and handler

`CameraView` is the cross-platform MAUI view. Its bindable properties are mapped by `CameraViewHandler` to a platform-specific `NativeCameraView`:

- Android uses Camera2, `TextureView`, and `ImageReader`.
- iOS uses `AVCaptureSession`, `AVCaptureVideoPreviewLayer`, and `AVCaptureVideoDataOutput`.

Changing camera, orientation, or enabled state increments a configuration version. Asynchronous permission and camera-start work verifies this version before touching the current native view. Stale operations therefore cannot restart a camera after a newer configuration has been applied.

## Lifecycle

The handler subscribes to the owning MAUI `Window` while the view is loaded.

- `Deactivated` invalidates pending work and stops the native session.
- `Activated` reapplies the latest configuration.
- `Unloaded` stops capture and detaches window events.
- Handler disconnection performs the same cleanup.

This keeps camera ownership aligned with application visibility and recovers from native session loss after screen lock.

## Frame pipeline

Android requests a JPEG output from `ImageReader`. iOS converts captured pixel buffers to JPEG through Core Image and UIKit. Both implementations invoke the managed callback from their capture queue.

Consumers should avoid expensive synchronous work in `OnFrameResult`. Copy or enqueue the byte array when additional processing is required, and marshal only UI updates to the main thread.
