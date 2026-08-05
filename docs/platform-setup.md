# Platform setup

## Android

### Requirements

- Android API 28 or later.
- A device or emulator with an available camera.

The package contributes these entries to the merged application manifest:

```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-feature
    android:name="android.hardware.camera.any"
    android:required="false" />
```

The feature is optional so an application can still be installed on devices without a camera. Decide whether the surrounding application requires a camera and adjust its own manifest or store declarations if needed.

The control requests the runtime camera permission before opening Camera2. A denied permission leaves the preview stopped.

## iOS

### Requirements

- iOS 15.0 or later.
- A physical device for complete camera testing. The simulator does not provide the same camera behavior as a device.

Add a purpose string to the application `Info.plist`:

```xml
<key>NSCameraUsageDescription</key>
<string>The camera is used to capture images.</string>
```

iOS terminates an application that accesses the camera without this key. The text is visible to users, so describe the application's actual use of the camera.

## Lifecycle behavior

On both platforms the control:

1. Stops and disposes the native camera session when the MAUI window is deactivated.
2. Invalidates any permission or start operation that is still pending.
3. Recreates the session when the window is activated again.
4. Restarts only when the view remains loaded and `Enabled` is `true`.

This behavior covers normal backgrounding and screen lock/unlock. Applications should still set `Enabled` to `false` when navigating away from a camera page if that page remains loaded.
