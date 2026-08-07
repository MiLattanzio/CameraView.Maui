namespace CameraView.Maui;

public sealed class CameraCaptureConfigurationChangedEventArgs : EventArgs
{
    internal CameraCaptureConfigurationChangedEventArgs(
        CameraCaptureConfiguration previousConfiguration,
        CameraCaptureConfiguration configuration)
    {
        PreviousConfiguration = previousConfiguration;
        Configuration = configuration;
    }

    public CameraCaptureConfiguration PreviousConfiguration { get; }

    public CameraCaptureConfiguration Configuration { get; }
}
