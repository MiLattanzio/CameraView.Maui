namespace CameraView.Maui;

public partial class CameraViewHandler
{
    private partial NativeCameraView CreateNativeCameraView() =>
        new(Context);
}
