namespace CameraView.Maui.TestApp;

public partial class MainPage : ContentPage
{
    private int _frameCount;
    private long _lastStatusUpdate;

    public MainPage()
    {
        InitializeComponent();
        CameraPreview.FrameAvailable += OnFrameAvailable;
        CameraPreview.StateChanged += OnCameraStateChanged;
        CameraPreview.ErrorOccurred += OnCameraError;
        CameraPreview.EffectiveConfigurationChanged += OnEffectiveConfigurationChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CameraPreview.Enabled = true;
        ToggleButton.Text = "Disattiva";
    }

    protected override void OnDisappearing()
    {
        CameraPreview.Enabled = false;
        base.OnDisappearing();
    }

    private void OnFrameAvailable(object sender, CameraFrameEventArgs eventArgs)
    {
        var frame = eventArgs.Frame;
        var frameCount = Interlocked.Increment(ref _frameCount);
        var now = Environment.TickCount64;
        var previousUpdate = Interlocked.Read(ref _lastStatusUpdate);
        if (now - previousUpdate < 500 ||
            Interlocked.CompareExchange(ref _lastStatusUpdate, now, previousUpdate) != previousUpdate)
            return;

        var bytes = frame.Planes.Sum(plane => plane.Length);
        MainThread.BeginInvokeOnMainThread(() =>
            StatusLabel.Text =
                $"Frame {frame.SequenceNumber:N0} - {frame.Width}x{frame.Height} - " +
                $"{frame.Format} - {frame.Planes.Count} planes - {bytes:N0} bytes");
    }

    private void OnCameraStateChanged(
        object sender,
        CameraStateChangedEventArgs eventArgs)
    {
        StateLabel.Text = $"Stato: {eventArgs.State} - Camera: {eventArgs.Camera}";
        ToggleButton.Text = eventArgs.State == CameraState.Stopped ? "Attiva" : "Disattiva";

        if (eventArgs.State is CameraState.Starting or CameraState.Running)
        {
            ErrorLabel.IsVisible = false;
            ErrorLabel.Text = string.Empty;
        }
    }

    private void OnCameraError(object sender, CameraErrorEventArgs eventArgs)
    {
        ErrorLabel.Text =
            $"{eventArgs.Code}: {eventArgs.Message} (recuperabile: {eventArgs.IsRecoverable})";
        ErrorLabel.IsVisible = true;
    }

    private void OnEffectiveConfigurationChanged(
        object sender,
        CameraCaptureConfigurationChangedEventArgs eventArgs)
    {
        var configuration = eventArgs.Configuration;
        if (configuration is null)
        {
            ConfigurationLabel.Text = "Configurazione: in attesa";
            return;
        }

        var quality = configuration.JpegQuality?.ToString() ?? "n/a";
        var fallback = configuration.UsedResolutionFallback ? " - fallback" : string.Empty;
        ConfigurationLabel.Text =
            $"Capture {configuration.CaptureResolution} - Preview " +
            $"{configuration.PreviewResolution} - {configuration.FrameFormat} - " +
            $"native {configuration.NativeFrameRate?.ToString() ?? "platform"} - " +
            $"delivery {configuration.MaximumFrameRate:0.##} fps - JPEG {quality}{fallback}";
    }

    private void OnSwitchCameraClicked(object sender, EventArgs e) =>
        CameraPreview.Camera = CameraPreview.Camera == CameraOptions.Rear
            ? CameraOptions.Front
            : CameraOptions.Rear;

    private void OnToggleCameraClicked(object sender, EventArgs e)
    {
        CameraPreview.Enabled = !CameraPreview.Enabled;
    }

    private void OnToggleOrientationClicked(object sender, EventArgs e)
    {
        CameraPreview.Orientation = CameraPreview.Orientation == CameraOrientation.Portrait
            ? CameraOrientation.Landscape
            : CameraOrientation.Portrait;
        OrientationButton.Text = CameraPreview.Orientation == CameraOrientation.Portrait
            ? "Landscape"
            : "Portrait";
    }

    private void OnCaptureProfileClicked(object sender, EventArgs eventArgs)
    {
        if (sender is not Button { CommandParameter: string profile })
            return;

        CameraPreview.CaptureOptions = profile switch
        {
            "Low" => CameraCaptureOptions.LowBandwidth,
            "Balanced" => CameraCaptureOptions.Balanced,
            "Realtime" => CameraCaptureOptions.Realtime,
            "Custom" => CameraCaptureOptions.HighQuality with
            {
                PreferredResolution = new CameraResolution(1600, 1200),
                ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost,
                MaximumFrameRate = 8,
                MinimumFrameInterval = TimeSpan.FromMilliseconds(150)
            },
            _ => CameraCaptureOptions.Default
        };
    }
}
