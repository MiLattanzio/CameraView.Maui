# CameraView.Maui

CameraView.Maui is a .NET MAUI camera preview control for Android and iOS. It uses Camera2 and AVFoundation and emits encoded JPEG frames through `OnFrameResult`.

## Supported platforms

| Platform | Minimum version |
| --- | --- |
| Android | API 28 |
| iOS | 15.0 |

## Installation

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

## XAML usage

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

Subscribe to `OnFrameResult` to receive JPEG frames:

```csharp
CameraPreview.OnFrameResult += result =>
{
    if (!result.Success || result.Image is null)
        return;

    // Frame callbacks are raised from a native capture queue.
    MainThread.BeginInvokeOnMainThread(() =>
    {
        // Update UI state here.
    });
};
```

Set `Enabled` to `false` when preview or capture is not required. The control releases the camera while the app is inactive and restarts it when the app resumes if `Enabled` remains `true`.

## Permissions

The package contributes `android.permission.CAMERA` and the optional `android.hardware.camera.any` feature to the merged Android manifest.

iOS applications must add a user-facing description to `Info.plist`:

```xml
<key>NSCameraUsageDescription</key>
<string>The camera is used to capture images.</string>
```

## More information

Full documentation, troubleshooting, and release notes are available in the [GitHub repository](https://github.com/MiLattanzio/CameraView.Maui/tree/master/docs).
