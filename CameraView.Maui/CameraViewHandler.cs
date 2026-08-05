using System.Diagnostics;
using Microsoft.Maui.Handlers;

namespace CameraView.Maui;

public partial class CameraViewHandler : ViewHandler<CameraView, NativeCameraView>
{
    private readonly SemaphoreSlim _configurationLock = new(1, 1);
    private Window _window;
    private int _configurationVersion;

    public static readonly IPropertyMapper<CameraView, CameraViewHandler> Mapper =
        new PropertyMapper<CameraView, CameraViewHandler>(ViewMapper)
        {
            [nameof(CameraView.Camera)] = MapConfiguration,
            [nameof(CameraView.Orientation)] = MapConfiguration,
            [nameof(CameraView.Enabled)] = MapConfiguration
        };

    public CameraViewHandler() : base(Mapper)
    {
    }

    protected override NativeCameraView CreatePlatformView() => CreateNativeCameraView();

    protected override void ConnectHandler(NativeCameraView platformView)
    {
        base.ConnectHandler(platformView);

        VirtualView.Loaded += OnVirtualViewLoaded;
        VirtualView.Unloaded += OnVirtualViewUnloaded;
        AttachToWindow(VirtualView.Window);
        ApplyConfiguration();
    }

    protected override void DisconnectHandler(NativeCameraView platformView)
    {
        VirtualView.Loaded -= OnVirtualViewLoaded;
        VirtualView.Unloaded -= OnVirtualViewUnloaded;
        AttachToWindow(null);
        SuspendConfiguration();
        base.DisconnectHandler(platformView);
    }

    private static void MapConfiguration(CameraViewHandler handler, CameraView view) =>
        handler.ApplyConfiguration();

    private void OnVirtualViewLoaded(object sender, EventArgs eventArgs)
    {
        AttachToWindow(VirtualView?.Window);
        ApplyConfiguration();
    }

    private void OnVirtualViewUnloaded(object sender, EventArgs eventArgs)
    {
        AttachToWindow(null);
        SuspendConfiguration();
    }

    private void AttachToWindow(Window window)
    {
        if (ReferenceEquals(_window, window))
            return;

        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
        }

        _window = window;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    private void OnWindowActivated(object sender, EventArgs eventArgs) =>
        ApplyConfiguration();

    private void OnWindowDeactivated(object sender, EventArgs eventArgs) =>
        SuspendConfiguration();

    private void SuspendConfiguration()
    {
        Interlocked.Increment(ref _configurationVersion);
        PlatformView?.Stop();
    }

    private void ApplyConfiguration()
    {
        var platformView = PlatformView;
        var cameraView = VirtualView;
        if (platformView is null || cameraView is null)
            return;

        var version = Interlocked.Increment(ref _configurationVersion);
        if (!cameraView.Enabled)
        {
            platformView.Stop();
            return;
        }

        platformView.Stop();
        _ = StartAsync(platformView, cameraView, version);
    }

    private async Task StartAsync(NativeCameraView platformView, CameraView cameraView, int version)
    {
        await _configurationLock.WaitAsync();
        try
        {
            if (!IsCurrentConfiguration(platformView, cameraView, version))
                return;

            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            if (status != PermissionStatus.Granted ||
                !IsCurrentConfiguration(platformView, cameraView, version))
                return;

            platformView.Start(cameraView.Camera, cameraView.Orientation, cameraView.SetResult);
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(platformView, PlatformView))
                platformView.Stop();

            Debug.WriteLine($"Unable to start the camera: {exception}");
        }
        finally
        {
            _configurationLock.Release();
        }
    }

    private bool IsCurrentConfiguration(
        NativeCameraView platformView,
        CameraView cameraView,
        int version) =>
        version == Volatile.Read(ref _configurationVersion) &&
        ReferenceEquals(platformView, PlatformView) &&
        ReferenceEquals(cameraView, VirtualView) &&
        cameraView.Enabled;

    private partial NativeCameraView CreateNativeCameraView();
}
