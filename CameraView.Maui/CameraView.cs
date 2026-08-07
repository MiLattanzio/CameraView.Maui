namespace CameraView.Maui;

public sealed class CameraView : Microsoft.Maui.Controls.View
{
    private static readonly BindablePropertyKey StatePropertyKey = BindableProperty.CreateReadOnly(
        nameof(State),
        typeof(CameraState),
        typeof(CameraView),
        CameraState.Stopped);

    private static readonly BindablePropertyKey EffectiveConfigurationPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(EffectiveConfiguration),
            typeof(CameraCaptureConfiguration),
            typeof(CameraView),
            null);

    private static readonly BindablePropertyKey EffectiveControlsPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(EffectiveControls),
            typeof(CameraControlState),
            typeof(CameraView),
            null);

    public static readonly BindableProperty CameraProperty = BindableProperty.Create(
        nameof(Camera),
        typeof(CameraOptions),
        typeof(CameraView),
        CameraOptions.Rear);

    public static readonly BindableProperty OrientationProperty = BindableProperty.Create(
        nameof(Orientation),
        typeof(CameraOrientation),
        typeof(CameraView),
        CameraOrientation.Landscape);

    public static readonly BindableProperty EnabledProperty = BindableProperty.Create(
        nameof(Enabled),
        typeof(bool),
        typeof(CameraView),
        true);

    public static readonly BindableProperty CaptureOptionsProperty = BindableProperty.Create(
        nameof(CaptureOptions),
        typeof(CameraCaptureOptions),
        typeof(CameraView),
        CameraCaptureOptions.Default,
        propertyChanging: (_, _, newValue) =>
        {
            if (newValue is not CameraCaptureOptions options)
                throw new ArgumentNullException(nameof(CaptureOptions));

            options.Validate();
        });

    public static readonly BindableProperty ControlOptionsProperty = BindableProperty.Create(
        nameof(ControlOptions),
        typeof(CameraControlOptions),
        typeof(CameraView),
        CameraControlOptions.Default,
        propertyChanging: (_, _, newValue) =>
        {
            if (newValue is not CameraControlOptions options)
                throw new ArgumentNullException(nameof(ControlOptions));

            options.Validate();
        });

    public static readonly BindableProperty StateProperty = StatePropertyKey.BindableProperty;

    public static readonly BindableProperty EffectiveConfigurationProperty =
        EffectiveConfigurationPropertyKey.BindableProperty;

    public static readonly BindableProperty EffectiveControlsProperty =
        EffectiveControlsPropertyKey.BindableProperty;

    // Keep the original field names for source compatibility with the Xamarin control.
    public static readonly BindableProperty CameraPreview = OrientationProperty;
    public static readonly BindableProperty CameraEnable = EnabledProperty;

    private long _sequenceNumber;

    public CameraOptions Camera
    {
        get => (CameraOptions)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    public CameraOrientation Orientation
    {
        get => (CameraOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public bool Enabled
    {
        get => (bool)GetValue(EnabledProperty);
        set => SetValue(EnabledProperty, value);
    }

    public CameraCaptureOptions CaptureOptions
    {
        get => (CameraCaptureOptions)GetValue(CaptureOptionsProperty);
        set => SetValue(CaptureOptionsProperty, value);
    }

    public CameraControlOptions ControlOptions
    {
        get => (CameraControlOptions)GetValue(ControlOptionsProperty);
        set => SetValue(ControlOptionsProperty, value);
    }

    public CameraCaptureConfiguration EffectiveConfiguration =>
        (CameraCaptureConfiguration)GetValue(EffectiveConfigurationProperty);

    public CameraControlState EffectiveControls =>
        (CameraControlState)GetValue(EffectiveControlsProperty);

    public CameraState State => (CameraState)GetValue(StateProperty);

    public bool IsRunning => State == CameraState.Running;

    public delegate void CameraResultEventHandler(CameraResult result);

    public event CameraResultEventHandler OnFrameResult;

    public event EventHandler<CameraFrameEventArgs> FrameAvailable;

    public event EventHandler<CameraStateChangedEventArgs> StateChanged;

    public event EventHandler<CameraErrorEventArgs> ErrorOccurred;

    public event EventHandler<CameraCaptureConfigurationChangedEventArgs>
        EffectiveConfigurationChanged;

    public event EventHandler<CameraControlStateChangedEventArgs>
        EffectiveControlsChanged;

    public void SetResult(byte[] image)
    {
        if (image is { Length: > 0 })
        {
            SetFrame(
                new ManagedCameraFrameBuffer(image),
                CameraFrameFormat.Jpeg,
                0,
                0,
                DateTimeOffset.UtcNow,
                Orientation,
                Camera,
                null,
                0,
                false);
        }
    }

    public void Cancel() => InvokeFrameResult(new CameraResult());

    internal void SetFrame(
        CameraFrameBuffer buffer,
        CameraFrameFormat format,
        int width,
        int height,
        DateTimeOffset timestamp,
        CameraOrientation orientation,
        CameraOptions camera,
        CameraCaptureConfiguration configuration,
        int rotationDegrees,
        bool isMirrored)
    {
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        using var frame = new CameraFrame(
            buffer,
            format,
            width,
            height,
            timestamp,
            orientation,
            camera,
            sequenceNumber,
            configuration,
            rotationDegrees,
            isMirrored);

        InvokeFrameAvailable(frame);

        var image = buffer.EncodedImage;
        if (format == CameraFrameFormat.Jpeg && image is { Length: > 0 })
        {
            InvokeFrameResult(new CameraResult(
                image,
                width,
                height,
                timestamp,
                orientation,
                camera,
                sequenceNumber,
                configuration));
        }
    }

    internal void SetEffectiveConfiguration(CameraCaptureConfiguration configuration)
    {
        var previousConfiguration = EffectiveConfiguration;
        if (ReferenceEquals(previousConfiguration, configuration))
            return;

        SetValue(EffectiveConfigurationPropertyKey, configuration);
        InvokeSafely(
            EffectiveConfigurationChanged,
            new CameraCaptureConfigurationChangedEventArgs(
                previousConfiguration,
                configuration));
    }

    internal void SetEffectiveControls(CameraControlState state)
    {
        var previousState = EffectiveControls;
        if (ReferenceEquals(previousState, state))
            return;

        SetValue(EffectiveControlsPropertyKey, state);
        InvokeSafely(
            EffectiveControlsChanged,
            new CameraControlStateChangedEventArgs(previousState, state));
    }

    internal void SetCameraState(CameraState state)
    {
        var previousState = State;
        if (previousState == state)
            return;

        SetValue(StatePropertyKey, state);
        OnPropertyChanged(nameof(IsRunning));
        InvokeSafely(
            StateChanged,
            new CameraStateChangedEventArgs(previousState, state, Camera));
    }

    internal void ReportCameraFailure(CameraState state, CameraFailure failure)
    {
        SetCameraState(state);
        ReportCameraError(failure);
    }

    internal void ReportCameraError(CameraFailure failure)
    {
        InvokeSafely(
            ErrorOccurred,
            new CameraErrorEventArgs(
                failure.Code,
                failure.Message,
                Camera,
                failure.IsRecoverable,
                failure.PlatformCode,
                failure.Exception));
    }

    private void InvokeFrameResult(CameraResult result)
    {
        var handlers = OnFrameResult;
        if (handlers is null)
            return;

        foreach (CameraResultEventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(result);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Camera frame subscriber failed: {exception}");
            }
        }
    }

    private void InvokeFrameAvailable(CameraFrame frame)
    {
        var handlers = FrameAvailable;
        if (handlers is null)
            return;

        foreach (EventHandler<CameraFrameEventArgs> handler in handlers.GetInvocationList())
        {
            using var subscriberFrame = frame.Retain();
            try
            {
                handler(this, new CameraFrameEventArgs(subscriberFrame));
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Camera frame subscriber failed: {exception}");
            }
        }
    }

    private void InvokeSafely<TEventArgs>(
        EventHandler<TEventArgs> handlers,
        TEventArgs eventArgs)
        where TEventArgs : EventArgs
    {
        if (handlers is null)
            return;

        foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Camera event subscriber failed: {exception}");
            }
        }
    }
}
