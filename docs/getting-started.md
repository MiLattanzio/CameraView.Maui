# Getting started

## 1. Install the package

Add CameraView.Maui to the MAUI application project:

```shell
dotnet add package CameraView.Maui
```

The application must target a supported .NET 9 or .NET 10 MAUI Android or iOS framework.

## 2. Register the handler

Call `UseCameraView` while creating the MAUI app:

```csharp
using CameraView.Maui;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .UseCameraView();

    return builder.Build();
}
```

The control cannot create its native view until this handler is registered.

## 3. Configure platform permissions

Android permission metadata is merged from the package. On iOS, add `NSCameraUsageDescription` to the app's `Info.plist`:

```xml
<key>NSCameraUsageDescription</key>
<string>The camera is used to capture images.</string>
```

See [Platform setup](platform-setup.md) for details.

## 4. Add the control

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:camera="clr-namespace:CameraView.Maui;assembly=CameraView.Maui"
    x:Class="MyApp.CameraPage">

    <camera:CameraView
        x:Name="CameraPreview"
        Camera="Rear"
        Orientation="Portrait"
        HorizontalOptions="Fill"
        VerticalOptions="Fill" />
</ContentPage>
```

## 5. Receive frames

```csharp
public CameraPage()
{
    InitializeComponent();
    CameraPreview.OnFrameResult += OnFrameResult;
}

private void OnFrameResult(CameraResult result)
{
    if (!result.Success || result.Image is null)
        return;

    var jpeg = result.Image;
    MainThread.BeginInvokeOnMainThread(() =>
    {
        StatusLabel.Text = $"Received {jpeg.Length:N0} bytes";
    });
}
```

Frame callbacks run on a native capture queue. Marshal UI work to the MAUI main thread and keep the callback short so the camera pipeline is not delayed.

## 6. Observe camera state and errors

`StateChanged` and `ErrorOccurred` run through the MAUI dispatcher, so their handlers can update page controls directly:

```csharp
public CameraPage()
{
    InitializeComponent();
    CameraPreview.StateChanged += OnCameraStateChanged;
    CameraPreview.ErrorOccurred += OnCameraError;
}

private void OnCameraStateChanged(
    object sender,
    CameraStateChangedEventArgs args)
{
    StateLabel.Text = $"{args.State} ({args.Camera})";
}

private void OnCameraError(object sender, CameraErrorEventArgs args)
{
    ErrorLabel.Text = $"{args.Code}: {args.Message}";

    if (!args.IsRecoverable)
        RetryButton.IsEnabled = false;
}
```

Use `CameraPreview.State` for the current state and `CameraPreview.IsRunning` when only an active-session check is required. `Camera` remains the requested and selected camera position.

## Camera state

Switch cameras:

```csharp
CameraPreview.Camera = CameraOptions.Front;
```

Change output orientation:

```csharp
CameraPreview.Orientation = CameraOrientation.Landscape;
```

Stop and restart capture:

```csharp
CameraPreview.Enabled = false;
CameraPreview.Enabled = true;
```

When the app loses activation, the control releases the native camera. It automatically restores the configured camera after activation if `Enabled` is still `true`.
