# API reference

All public types are in the `CameraView.Maui` namespace.

## CameraView

A MAUI `View` that displays the native camera preview and emits JPEG frames.

### Properties

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Camera` | `CameraOptions` | `Rear` | Selects the front or rear camera. Changing it restarts capture. |
| `Orientation` | `CameraOrientation` | `Landscape` | Selects portrait or landscape frame orientation. Changing it restarts capture. |
| `Enabled` | `bool` | `true` | Controls whether the camera session should be active. |

### Event

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

The event is raised on the platform capture queue, not necessarily the UI thread.

### Methods

- `SetResult(byte[] image)` emits a successful result when the array is non-empty. It is primarily used by the native handler.
- `Cancel()` emits an unsuccessful result.

### Compatibility fields

`CameraPreview` and `CameraEnable` are bindable-property aliases retained for compatibility with the earlier Xamarin control. New code should use `OrientationProperty` and `EnabledProperty`.

## CameraResult

| Member | Type | Description |
| --- | --- | --- |
| `Success` | `bool` | Indicates whether an image is available. |
| `Image` | `byte[]` | JPEG bytes for a successful result; no image is assigned to a cancelled result. |

`new CameraResult()` creates an unsuccessful result. `new CameraResult(byte[])` creates a successful result and rejects a null array.

## CameraOptions

- `Rear`
- `Front`

## CameraOrientation

- `Portrait`
- `Landscape`

## CameraViewAppBuilderExtensions

`UseCameraView(MauiAppBuilder)` registers `CameraViewHandler` with the MAUI handler collection and returns the same builder.
