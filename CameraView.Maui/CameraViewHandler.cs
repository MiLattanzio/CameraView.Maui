using System.Diagnostics;
using Microsoft.Maui.Handlers;

namespace CameraView.Maui;

public partial class CameraViewHandler : ViewHandler<CameraView, NativeCameraView>
{
    private readonly SemaphoreSlim _configurationLock = new(1, 1);
    private Window _window;
    private int _configurationVersion;
    private bool _isLoaded;
    private bool _isWindowActive = true;

    public static readonly IPropertyMapper<CameraView, CameraViewHandler> Mapper =
        new PropertyMapper<CameraView, CameraViewHandler>(ViewMapper)
        {
            [nameof(CameraView.Camera)] = MapConfiguration,
            [nameof(CameraView.Orientation)] = MapConfiguration,
            [nameof(CameraView.Enabled)] = MapConfiguration,
            [nameof(CameraView.CaptureOptions)] = MapConfiguration
        };

    public CameraViewHandler() : base(Mapper)
    {
    }

    protected override NativeCameraView CreatePlatformView() => CreateNativeCameraView();

    protected override void ConnectHandler(NativeCameraView platformView)
    {
        base.ConnectHandler(platformView);

        _isLoaded = VirtualView.IsLoaded;
        _isWindowActive = true;
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
        _isLoaded = false;
        _isWindowActive = false;
        SuspendConfiguration(CameraState.Stopped);
        base.DisconnectHandler(platformView);
    }

    private static void MapConfiguration(CameraViewHandler handler, CameraView view) =>
        handler.ApplyConfiguration();

    private void OnVirtualViewLoaded(object sender, EventArgs eventArgs)
    {
        _isLoaded = true;
        _isWindowActive = true;
        AttachToWindow(VirtualView?.Window);
        ApplyConfiguration();
    }

    private void OnVirtualViewUnloaded(object sender, EventArgs eventArgs)
    {
        _isLoaded = false;
        AttachToWindow(null);
        SuspendConfiguration(CameraState.Suspended);
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

    private void OnWindowActivated(object sender, EventArgs eventArgs)
    {
        _isWindowActive = true;
        ApplyConfiguration();
    }

    private void OnWindowDeactivated(object sender, EventArgs eventArgs)
    {
        _isWindowActive = false;
        SuspendConfiguration(CameraState.Suspended);
    }

    private void SuspendConfiguration(CameraState state)
    {
        Interlocked.Increment(ref _configurationVersion);
        PlatformView?.Stop();

        var cameraView = VirtualView;
        if (cameraView is not null)
        {
            if (state == CameraState.Stopped)
                cameraView.SetEffectiveConfiguration(null);
            cameraView.SetCameraState(cameraView.Enabled ? state : CameraState.Stopped);
        }
    }

    private void ApplyConfiguration()
    {
        var platformView = PlatformView;
        var cameraView = VirtualView;
        if (platformView is null || cameraView is null)
            return;

        var version = Interlocked.Increment(ref _configurationVersion);
        platformView.Stop();
        cameraView.SetEffectiveConfiguration(null);

        if (!cameraView.Enabled)
        {
            cameraView.SetCameraState(CameraState.Stopped);
            return;
        }

        if (!_isLoaded || !_isWindowActive)
        {
            cameraView.SetCameraState(CameraState.Suspended);
            return;
        }

        cameraView.SetCameraState(CameraState.Starting);
        _ = StartAsync(platformView, cameraView, version);
    }

    private async Task StartAsync(
        NativeCameraView platformView,
        CameraView cameraView,
        int version)
    {
        await _configurationLock.WaitAsync();
        try
        {
            if (!IsCurrentConfiguration(platformView, cameraView, version))
                return;

            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            if (!IsCurrentConfiguration(platformView, cameraView, version))
                return;

            if (status != PermissionStatus.Granted)
            {
                DispatchFailure(
                    platformView,
                    cameraView,
                    version,
                    CameraState.PermissionDenied,
                    new CameraFailure(
                        CameraErrorCode.PermissionDenied,
                        "Camera permission was denied.",
                        true,
                        status.ToString()));
                return;
            }

            var captureOptions = cameraView.CaptureOptions;
            captureOptions.Validate();
            var selectedCamera = cameraView.Camera;
            var selectedOrientation = cameraView.Orientation;

            platformView.Start(
                selectedCamera,
                selectedOrientation,
                (buffer, format, width, height, timestamp, configuration,
                    rotationDegrees, isMirrored) =>
                    cameraView.SetFrame(
                        buffer,
                        format,
                        width,
                        height,
                        timestamp,
                        selectedOrientation,
                        selectedCamera,
                        configuration,
                        rotationDegrees,
                        isMirrored),
                captureOptions,
                configuration => DispatchEffectiveConfiguration(
                    platformView,
                    cameraView,
                    version,
                    configuration),
                () => DispatchState(
                    platformView,
                    cameraView,
                    version,
                    CameraState.Running),
                () => DispatchState(
                    platformView,
                    cameraView,
                    version,
                    CameraState.Suspended),
                failure => DispatchFailure(
                    platformView,
                    cameraView,
                    version,
                    GetFailureState(failure),
                    failure));
        }
        catch (CameraPlatformException exception)
        {
            DispatchFailure(
                platformView,
                cameraView,
                version,
                GetFailureState(exception.Failure),
                exception.Failure);
        }
        catch (Exception exception)
        {
            DispatchFailure(
                platformView,
                cameraView,
                version,
                CameraState.Failed,
                new CameraFailure(
                    CameraErrorCode.Unknown,
                    "Unable to start the camera.",
                    true,
                    exception.GetType().Name,
                    exception));
        }
        finally
        {
            _configurationLock.Release();
        }
    }

    private void DispatchState(
        NativeCameraView platformView,
        CameraView cameraView,
        int version,
        CameraState state) =>
        Dispatch(cameraView, () =>
        {
            if (IsCurrentConfiguration(platformView, cameraView, version))
                cameraView.SetCameraState(state);
        });

    private void DispatchEffectiveConfiguration(
        NativeCameraView platformView,
        CameraView cameraView,
        int version,
        CameraCaptureConfiguration configuration) =>
        Dispatch(cameraView, () =>
        {
            if (IsCurrentConfiguration(platformView, cameraView, version))
                cameraView.SetEffectiveConfiguration(configuration);
        });

    private void DispatchFailure(
        NativeCameraView platformView,
        CameraView cameraView,
        int version,
        CameraState state,
        CameraFailure failure) =>
        Dispatch(cameraView, () =>
        {
            if (!IsCurrentConfiguration(platformView, cameraView, version))
                return;

            Interlocked.Increment(ref _configurationVersion);
            platformView.Stop();
            cameraView.SetEffectiveConfiguration(null);
            cameraView.ReportCameraFailure(state, failure);
            Debug.WriteLine(
                $"Camera failure {failure.Code} ({failure.PlatformCode}): {failure.Message} {failure.Exception}");
        });

    private static void Dispatch(CameraView cameraView, Action action)
    {
        if (cameraView.Dispatcher.IsDispatchRequired)
            cameraView.Dispatcher.Dispatch(action);
        else
            action();
    }

    private static CameraState GetFailureState(CameraFailure failure) =>
        failure.Code == CameraErrorCode.PermissionDenied
            ? CameraState.PermissionDenied
            : CameraState.Failed;

    private bool IsCurrentConfiguration(
        NativeCameraView platformView,
        CameraView cameraView,
        int version) =>
        version == Volatile.Read(ref _configurationVersion) &&
        ReferenceEquals(platformView, PlatformView) &&
        ReferenceEquals(cameraView, VirtualView) &&
        cameraView.Enabled &&
        _isLoaded &&
        _isWindowActive;

    private partial NativeCameraView CreateNativeCameraView();
}
