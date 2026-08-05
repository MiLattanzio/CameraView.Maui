using CameraView.Maui;
using Microsoft.Maui.Hosting;
using CameraControl = CameraView.Maui.CameraView;

namespace CameraViewPackageSmokeTest;

public static class PackageConsumer
{
    public static MauiAppBuilder Register(MauiAppBuilder builder) =>
        builder.UseCameraView();

    public static CameraControl CreatePreview(Action<byte[]> consumeFrame)
    {
        ArgumentNullException.ThrowIfNull(consumeFrame);

        var preview = new CameraControl
        {
            Camera = CameraOptions.Rear,
            Orientation = CameraOrientation.Portrait,
            Enabled = true
        };

        preview.OnFrameResult += result =>
        {
            if (result.Success && result.Image is { Length: > 0 })
                consumeFrame(result.Image);
        };

        return preview;
    }

    public static void Restart(CameraControl preview)
    {
        preview.Enabled = false;
        preview.Enabled = true;
    }
}
