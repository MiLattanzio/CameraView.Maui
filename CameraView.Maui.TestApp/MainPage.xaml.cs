namespace CameraView.Maui.TestApp;

public partial class MainPage : ContentPage
{
    private int _frameCount;
    private long _lastStatusUpdate;

    public MainPage()
    {
        InitializeComponent();
        CameraPreview.OnFrameResult += OnFrameResult;
        CameraPreview.StateChanged += OnCameraStateChanged;
        CameraPreview.ErrorOccurred += OnCameraError;
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

    private void OnFrameResult(CameraResult result)
    {
        if (!result.Success || result.Image is null)
            return;

        var frameCount = Interlocked.Increment(ref _frameCount);
        var now = Environment.TickCount64;
        var previousUpdate = Interlocked.Read(ref _lastStatusUpdate);
        if (now - previousUpdate < 500 ||
            Interlocked.CompareExchange(ref _lastStatusUpdate, now, previousUpdate) != previousUpdate)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
            StatusLabel.Text = $"Frame {frameCount:N0} · {result.Image.Length:N0} byte");
    }

    private void OnCameraStateChanged(
        object sender,
        CameraStateChangedEventArgs eventArgs)
    {
        StateLabel.Text = $"Stato: {eventArgs.State} · Camera: {eventArgs.Camera}";
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
}
