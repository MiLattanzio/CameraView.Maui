namespace CameraView.Maui.TestApp;

public partial class MainPage : ContentPage
{
    private int _frameCount;
    private long _lastStatusUpdate;
    private bool _updatingControlUi;

    public MainPage()
    {
        InitializeComponent();
        CameraPreview.FrameAvailable += OnFrameAvailable;
        CameraPreview.StateChanged += OnCameraStateChanged;
        CameraPreview.ErrorOccurred += OnCameraError;
        CameraPreview.EffectiveConfigurationChanged += OnEffectiveConfigurationChanged;
        CameraPreview.EffectiveControlsChanged += OnEffectiveControlsChanged;
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

    private void OnEffectiveControlsChanged(
        object sender,
        CameraControlStateChangedEventArgs eventArgs)
    {
        var controls = eventArgs.State;
        if (controls is null)
        {
            ControlsLabel.Text = "Controlli: in attesa";
            return;
        }

        _updatingControlUi = true;
        try
        {
            var capabilities = controls.Capabilities;
            ZoomSlider.Minimum = capabilities.MinimumZoomFactor;
            ZoomSlider.Maximum = capabilities.MaximumZoomFactor;
            ZoomSlider.Value = controls.ZoomFactor;
            ZoomSlider.IsEnabled = capabilities.IsZoomSupported;
            ExposureSlider.Minimum = capabilities.MinimumExposureCompensation;
            ExposureSlider.Maximum = capabilities.MaximumExposureCompensation;
            ExposureSlider.Value = controls.ExposureCompensation;
            ExposureSlider.IsEnabled = capabilities.SupportsExposureCompensation;
            TorchButton.IsEnabled = capabilities.IsTorchSupported;
            TorchButton.Text = controls.TorchEnabled ? "Torcia: on" : "Torcia: off";
            ZoomLabel.Text = $"{controls.ZoomFactor:0.00}x";
            ExposureLabel.Text = $"{controls.ExposureCompensation:+0.00;-0.00;0.00}";
            MirroringButton.Text = $"Mirror: {CameraPreview.ControlOptions.PreviewMirroring.ToString().ToLowerInvariant()}";
            ControlsLabel.Text =
                $"Zoom {capabilities.MinimumZoomFactor:0.##}-{capabilities.MaximumZoomFactor:0.##}x - " +
                $"EV {capabilities.MinimumExposureCompensation:0.##}..{capabilities.MaximumExposureCompensation:0.##} - " +
                $"focus {controls.FocusMode?.ToString() ?? "n/a"}" +
                (controls.FocusPoint.HasValue ? $" @ {controls.FocusPoint.Value}" : string.Empty) +
                $" - mirror {(controls.IsPreviewMirrored ? "on" : "off")}";
        }
        finally
        {
            _updatingControlUi = false;
        }
    }

    private void OnZoomChanged(object sender, ValueChangedEventArgs eventArgs)
    {
        if (_updatingControlUi)
            return;

        ZoomLabel.Text = $"{eventArgs.NewValue:0.00}x";
        CameraPreview.ControlOptions = CameraPreview.ControlOptions with
        {
            ZoomFactor = eventArgs.NewValue
        };
    }

    private void OnExposureChanged(object sender, ValueChangedEventArgs eventArgs)
    {
        if (_updatingControlUi)
            return;

        ExposureLabel.Text = $"{eventArgs.NewValue:+0.00;-0.00;0.00}";
        CameraPreview.ControlOptions = CameraPreview.ControlOptions with
        {
            ExposureCompensation = eventArgs.NewValue
        };
    }

    private void OnToggleTorchClicked(object sender, EventArgs eventArgs)
    {
        CameraPreview.ControlOptions = CameraPreview.ControlOptions with
        {
            TorchEnabled = !CameraPreview.ControlOptions.TorchEnabled
        };
    }

    private void OnResetFocusClicked(object sender, EventArgs eventArgs)
    {
        CameraPreview.ControlOptions = CameraPreview.ControlOptions with
        {
            FocusMode = CameraFocusMode.Continuous,
            FocusPoint = null
        };
    }

    private void OnPreviewTapped(object sender, TappedEventArgs eventArgs)
    {
        var position = eventArgs.GetPosition(CameraPreview);
        if (!position.HasValue || CameraPreview.Width <= 0 || CameraPreview.Height <= 0)
            return;

        CameraPreview.ControlOptions = CameraPreview.ControlOptions with
        {
            FocusMode = CameraFocusMode.Single,
            FocusPoint = new CameraPoint(
                Math.Clamp(position.Value.X / CameraPreview.Width, 0, 1),
                Math.Clamp(position.Value.Y / CameraPreview.Height, 0, 1))
        };
    }

    private void OnToggleMirroringClicked(object sender, EventArgs eventArgs)
    {
        var mirroring = CameraPreview.ControlOptions.PreviewMirroring switch
        {
            CameraPreviewMirroringMode.Automatic => CameraPreviewMirroringMode.Mirrored,
            CameraPreviewMirroringMode.Mirrored => CameraPreviewMirroringMode.Unmirrored,
            _ => CameraPreviewMirroringMode.Automatic
        };
        CameraPreview.ControlOptions = CameraPreview.ControlOptions with
        {
            PreviewMirroring = mirroring
        };
    }

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
