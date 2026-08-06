namespace CameraView.Maui;

public sealed class CameraView : Microsoft.Maui.Controls.View
{
    private static readonly BindablePropertyKey StatePropertyKey = BindableProperty.CreateReadOnly(
        nameof(State),
        typeof(CameraState),
        typeof(CameraView),
        CameraState.Stopped);
    private static readonly BindablePropertyKey EffectiveConfigurationPropertyKey = BindableProperty.CreateReadOnly(
        nameof(EffectiveConfiguration),
        typeof(CameraCaptureConfiguration),
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

    public static readonly BindableProperty ResolutionProperty = BindableProperty.Create(
        nameof(Resolution), typeof(CameraResolution), typeof(CameraView), CameraResolution.Default);
    public static readonly BindableProperty JpegQualityProperty = BindableProperty.Create(
        nameof(JpegQuality), typeof(int), typeof(CameraView), 85, propertyChanging: (_, oldValue, newValue) =>
        {
            var value = (int)newValue;
            if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(JpegQuality));
        });
    public static readonly BindableProperty MaximumFrameRateProperty = BindableProperty.Create(
        nameof(MaximumFrameRate), typeof(int), typeof(CameraView), 0, propertyChanging: (_, oldValue, newValue) =>
        {
            if ((int)newValue < 0) throw new ArgumentOutOfRangeException(nameof(MaximumFrameRate));
        });
    public static readonly BindableProperty MinimumFrameIntervalProperty = BindableProperty.Create(
        nameof(MinimumFrameInterval), typeof(TimeSpan), typeof(CameraView), TimeSpan.Zero, propertyChanging: (_, oldValue, newValue) =>
        {
            if ((TimeSpan)newValue < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MinimumFrameInterval));
        });

    public static readonly BindableProperty StateProperty = StatePropertyKey.BindableProperty;
    public static readonly BindableProperty EffectiveConfigurationProperty = EffectiveConfigurationPropertyKey.BindableProperty;

    // Keep the original field names for source compatibility with the Xamarin control.
    public static readonly BindableProperty CameraPreview = OrientationProperty;
    public static readonly BindableProperty CameraEnable = EnabledProperty;

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

    public CameraResolution Resolution { get => (CameraResolution)GetValue(ResolutionProperty); set => SetValue(ResolutionProperty, value); }
    public int JpegQuality { get => (int)GetValue(JpegQualityProperty); set => SetValue(JpegQualityProperty, value); }
    public int MaximumFrameRate { get => (int)GetValue(MaximumFrameRateProperty); set => SetValue(MaximumFrameRateProperty, value); }
    public TimeSpan MinimumFrameInterval { get => (TimeSpan)GetValue(MinimumFrameIntervalProperty); set => SetValue(MinimumFrameIntervalProperty, value); }
    public CameraCaptureConfiguration EffectiveConfiguration => (CameraCaptureConfiguration)GetValue(EffectiveConfigurationProperty);

    public CameraState State => (CameraState)GetValue(StateProperty);

    public bool IsRunning => State == CameraState.Running;

    public delegate void CameraResultEventHandler(CameraResult result);

    public event CameraResultEventHandler OnFrameResult;

    public event EventHandler<CameraStateChangedEventArgs> StateChanged;

    public event EventHandler<CameraErrorEventArgs> ErrorOccurred;

    public void SetResult(byte[] image)
    {
        if (image is { Length: > 0 })
            InvokeFrameResult(new CameraResult(image));
    }

    internal void SetResult(byte[] image, int width, int height)
    {
        if (image is not { Length: > 0 }) return;
        InvokeFrameResult(new CameraResult(image, width, height, DateTimeOffset.UtcNow, Orientation, Camera, Interlocked.Increment(ref _sequenceNumber)));
    }

    private long _sequenceNumber;

    internal void SetEffectiveConfiguration(CameraCaptureConfiguration configuration) =>
        SetValue(EffectiveConfigurationPropertyKey, configuration);

    public void Cancel() => InvokeFrameResult(new CameraResult());

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
