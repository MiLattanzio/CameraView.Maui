# Troubleshooting

## The preview is black

Check the following:

1. `UseCameraView()` is called in `MauiProgram.cs`.
2. Camera permission is granted in system settings.
3. iOS `Info.plist` contains `NSCameraUsageDescription`.
4. `CameraView.Enabled` is `true`.
5. The requested front or rear camera exists on the device.
6. No other application is currently holding the camera.

After screen lock or backgrounding, the control should restart automatically when the app is activated. If it does not, collect platform logs around the deactivate/activate transition and include device model, OS version, and a minimal reproduction in the issue.

## The application closes when camera permission is requested on iOS

Add `NSCameraUsageDescription` to the app's `Info.plist`. iOS requires a purpose string before camera access.

## No frames arrive

The preview and frame output share the same session. Confirm the preview is active, keep a strong subscription to `OnFrameResult`, and avoid blocking the capture callback.

Frame delivery is intentionally throttled by the native camera and processing capacity; it is not guaranteed to match the sensor's maximum frame rate.

## UI updates throw or behave inconsistently

`OnFrameResult` is raised from a native capture queue. Use `MainThread.BeginInvokeOnMainThread` before updating controls or other UI-bound state.

## Android emulator issues

Configure a camera source in the emulator's extended controls. For lifecycle, orientation, and hardware-specific behavior, verify on a physical device.

## iOS simulator limitations

Use a physical iPhone or iPad for camera behavior. A successful simulator build confirms compilation, not physical camera capture.
