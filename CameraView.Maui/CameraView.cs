namespace CameraView.Maui;

public sealed class CameraView : Microsoft.Maui.Controls.View
{
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

    public delegate void CameraResultEventHandler(CameraResult result);

    public event CameraResultEventHandler OnFrameResult;

    public void SetResult(byte[] image)
    {
        if (image is { Length: > 0 })
            OnFrameResult?.Invoke(new CameraResult(image));
    }

    public void Cancel() => OnFrameResult?.Invoke(new CameraResult());
}
