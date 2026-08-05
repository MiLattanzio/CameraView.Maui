namespace CameraView.Maui;

public static class CameraViewAppBuilderExtensions
{
    public static MauiAppBuilder UseCameraView(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<CameraView, CameraViewHandler>());

        return builder;
    }
}
