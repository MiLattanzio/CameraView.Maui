namespace CameraView.Maui;

public sealed class CameraView : Microsoft.Maui.Controls.View
{
    private static readonly BindablePropertyKey StatePropertyKey = BindableProperty.CreateReadOnly(
        nameof(State),
        typeof(CameraState),
        typeof(CameraView),
        CameraState.Stopped);

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

    public static readonly BindableProperty StateProperty = StatePropertyKey.BindableProperty;

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
