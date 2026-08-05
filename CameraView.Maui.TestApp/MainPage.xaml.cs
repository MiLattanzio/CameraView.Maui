namespace CameraView.Maui.TestApp;

public partial class MainPage : ContentPage
{
    private int _frameCount;
    private long _lastStatusUpdate;

    public MainPage()
    {
        InitializeComponent();
        CameraPreview.OnFrameResult += OnFrameResult;
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

    private void OnSwitchCameraClicked(object sender, EventArgs e) =>
        CameraPreview.Camera = CameraPreview.Camera == CameraOptions.Rear
            ? CameraOptions.Front
            : CameraOptions.Rear;

    private void OnToggleCameraClicked(object sender, EventArgs e)
    {
        CameraPreview.Enabled = !CameraPreview.Enabled;
        ToggleButton.Text = CameraPreview.Enabled ? "Disattiva" : "Attiva";
        StatusLabel.Text = CameraPreview.Enabled ? "Avvio camera…" : "Camera disattivata";
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
