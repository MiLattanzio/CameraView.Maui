# Troubleshooting

## Start with the observable state

Version 1.1.0 and later expose `StateChanged` and `ErrorOccurred`. Log both before collecting the full platform trace:

```csharp
CameraPreview.StateChanged += (_, args) =>
    System.Diagnostics.Debug.WriteLine(
        $"Camera {args.PreviousState} -> {args.State} ({args.Camera})");

CameraPreview.ErrorOccurred += (_, args) =>
    System.Diagnostics.Debug.WriteLine(
        $"Camera error {args.Code}; native={args.PlatformCode}; " +
        $"recoverable={args.IsRecoverable}; {args.Message}; {args.Exception}");
```

`PermissionDenied` requires a permission/settings change. `CameraInUse` means another session must release the camera. `CameraUnavailable`, `DeviceDisconnected`, and `SessionConfigurationFailed` usually require a lifecycle retry, camera switch, or device-level investigation. `CaptureFailed` identifies a failure after the session started.

## The preview is black

Check the following:

1. `UseCameraView()` is called in `MauiProgram.cs`.
2. Camera permission is granted in system settings.
3. iOS `Info.plist` contains `NSCameraUsageDescription`.
4. `CameraView.Enabled` is `true`.
5. The requested front or rear camera exists on the device.
6. No other application is currently holding the camera.

After screen lock or backgrounding, the control should restart automatically when the app is activated. If it does not, collect platform logs around the deactivate/activate transition and include device model, OS version, and a minimal reproduction in the issue.

As a diagnostic step, set `Enabled` to `false`, wait until the page is visible and active, and set it back to `true`. If this recovers the preview, include that result with the lifecycle logs; it helps distinguish session loss from permission or layout problems.

## Permission was denied or changed in system settings

The operating system can stop showing its permission prompt after a user denies access. Open the application's system settings and grant camera access manually, then fully reactivate the application.

For an Android development build, permission behavior can be reset with:

```shell
adb shell pm revoke your.application.id android.permission.CAMERA
```

On iOS, change camera access under **Settings > Privacy & Security > Camera**. Reinstalling a development build also resets its permission state. Always keep `NSCameraUsageDescription` in `Info.plist`; iOS terminates an application that requests camera access without it.

## The camera is unavailable or already in use

Only one active session may own a camera on many devices. Close camera, scanner, video-call, browser, and emulator tools before retrying. Also verify that the requested front or rear camera exists; tablets, emulators, managed devices, and damaged hardware may expose only one camera or none.

On Android, rapid application switching and some manufacturer camera services can delay resource release briefly. Let the application become fully active before retrying. On iOS, calls, FaceTime, Control Center, and other system capture sessions can interrupt camera access; return to the application and allow the MAUI window activation path to recreate the session.

Do not place two enabled `CameraView` controls on visible pages at the same time. Disable the old control before navigation makes the next one active.

## The application closes when camera permission is requested on iOS

Add `NSCameraUsageDescription` to the app's `Info.plist`. iOS requires a purpose string before camera access.

## No frames arrive

The preview and frame output share the same session. Confirm the preview is active, keep a strong subscription to `OnFrameResult`, and avoid blocking the capture callback.

Frame delivery is intentionally throttled by the native camera and processing capacity; it is not guaranteed to match the sensor's maximum frame rate.

## Requested capture resolution is not selected

Subscribe to `EffectiveConfigurationChanged` and inspect `CaptureResolution`, `PreviewResolution`, and `UsedResolutionFallback`. Device size lists vary by camera position, hardware model, active format, and platform version.

- `Closest` minimizes aspect-ratio and pixel-count differences.
- `AtMost` and `AtLeast` constrain dimensions first, then fall back to the closest size only when the constrained set is empty.
- `Exact` never falls back; an unavailable size produces `SessionConfigurationFailed` with platform code `ExactResolutionUnavailable`.

Use `CameraCaptureOptions.Default` to rule out a device-specific high-resolution combination. Configuration changes must replace `CaptureOptions`; its immutable properties cannot be changed in place.

## UI updates throw or behave inconsistently

`OnFrameResult` is raised from a native capture queue. Use `MainThread.BeginInvokeOnMainThread` before updating controls or other UI-bound state.

## Android emulator issues

Configure a camera source in the emulator's extended controls. For lifecycle, orientation, and hardware-specific behavior, verify on a physical device.

## iOS simulator limitations

Use a physical iPhone or iPad for camera behavior. A successful simulator build confirms compilation, not physical camera capture.

## Collect Android diagnostics

Clear the existing log, reproduce the problem once, and capture a bounded log:

```shell
adb logcat -c
adb logcat -v threadtime -d > camera-log.txt
```

Search the result for `CameraView`, `Camera2`, `CameraManager`, `CameraService`, `AndroidRuntime`, the application ID, and the time of the failure. Do not publish unrelated personal or device data from the log.

Also record:

- Device manufacturer and exact model.
- Android version and API level.
- Whether the problem occurs with the front camera, rear camera, or both.
- Permission state and whether another camera application works.
- The sequence of navigation, rotation, lock, background, and resume actions.

## Collect iOS diagnostics

Run the application from Xcode on a physical device and reproduce the problem while the debug console is attached. In **Window > Devices and Simulators**, select the device and open its console when the failure does not reproduce under the debugger.

Filter for the application process together with `AVCaptureSession`, `AVCaptureDevice`, `mediaserverd`, and `FigCapture`. Record the iPhone or iPad model, iOS version, camera position, permission state, and the exact interruption or lifecycle sequence.

## Report a reproducible issue

Include all of the following:

1. CameraView.Maui package version and .NET MAUI version.
2. A minimal page showing the `CameraView` declaration and event subscription.
3. Device, operating-system version, and selected camera.
4. Exact reproduction steps and expected versus actual behavior.
5. A short, sanitized platform log covering the failure.
6. Whether toggling `Enabled`, restarting the app, or restarting the device changes the result.

Lifecycle and camera bugs generally cannot be diagnosed from a black-preview screenshot alone.
