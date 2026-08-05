# CameraView.Maui

[![CI](https://github.com/MiLattanzio/CameraView.Maui/actions/workflows/ci.yml/badge.svg)](https://github.com/MiLattanzio/CameraView.Maui/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/CameraView.Maui.svg)](https://www.nuget.org/packages/CameraView.Maui)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

CameraView.Maui is a reusable .NET MAUI camera preview control for Android and iOS. It uses Camera2 and AVFoundation and exposes each captured frame as an encoded JPEG byte array.

## Features

- Native live preview on Android and iOS.
- Front and rear camera selection.
- Portrait and landscape output orientation.
- JPEG frame callbacks.
- Runtime permission requests.
- Automatic camera release and restart across app deactivation, screen lock, and resume.

## Requirements

- .NET 9 MAUI.
- Android API 28 or later.
- iOS 15.0 or later.

## Quick start

Install the package:

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

Add the control to XAML:

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

iOS applications must also add `NSCameraUsageDescription` to `Info.plist`. The Android camera permission is contributed by the library.

## Documentation

- [Getting started](docs/getting-started.md)
- [Platform setup](docs/platform-setup.md)
- [API reference](docs/api-reference.md)
- [Architecture and lifecycle](docs/architecture.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Changelog](docs/CHANGELOG.md)
- [Contributing](docs/CONTRIBUTING.md)
- [Release and Trusted Publishing](docs/releasing.md)

## Build

```shell
dotnet workload restore CameraView.Maui.sln
dotnet restore CameraView.Maui.sln
dotnet build CameraView.Maui.sln
```

## License

CameraView.Maui is licensed under the [MIT License](LICENSE).
