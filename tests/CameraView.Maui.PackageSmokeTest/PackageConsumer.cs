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
            CaptureOptions = CameraCaptureOptions.Balanced with
            {
                PreferredResolution = new CameraResolution(1024, 768),
                ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost,
                MaximumFrameRate = 12.5,
                MinimumFrameInterval = TimeSpan.FromMilliseconds(50)
            },
            Enabled = true
        };

        preview.OnFrameResult += result =>
        {
            if (result.Success && result.Image is { Length: > 0 })
            {
                _ = result.Width;
                _ = result.Height;
                _ = result.Timestamp;
                _ = result.Orientation;
                _ = result.Camera;
                _ = result.SequenceNumber;
                _ = result.Configuration;
                consumeFrame(result.Image);
            }
        };
        preview.StateChanged += (_, eventArgs) =>
        {
            _ = eventArgs.PreviousState;
            _ = eventArgs.State;
            _ = eventArgs.Camera;
        };
        preview.ErrorOccurred += (_, eventArgs) =>
        {
            _ = eventArgs.Code;
            _ = eventArgs.Message;
            _ = eventArgs.Camera;
            _ = eventArgs.IsRecoverable;
            _ = eventArgs.PlatformCode;
            _ = eventArgs.Exception;
        };
        preview.EffectiveConfigurationChanged += (_, eventArgs) =>
        {
            _ = eventArgs.PreviousConfiguration;
            _ = eventArgs.Configuration?.RequestedOptions;
            _ = eventArgs.Configuration?.CaptureResolution;
            _ = eventArgs.Configuration?.PreviewResolution;
            _ = eventArgs.Configuration?.JpegQuality;
            _ = eventArgs.Configuration?.MinimumFrameInterval;
            _ = eventArgs.Configuration?.MaximumFrameRate;
            _ = eventArgs.Configuration?.UsedResolutionFallback;
        };

        _ = preview.State;
        _ = preview.IsRunning;
        _ = preview.EffectiveConfiguration;

        return preview;
    }

    public static void Restart(CameraControl preview)
    {
        preview.Enabled = false;
        preview.Enabled = true;
    }
}
