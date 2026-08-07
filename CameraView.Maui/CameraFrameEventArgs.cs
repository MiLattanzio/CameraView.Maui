namespace CameraView.Maui;

public sealed class CameraFrameEventArgs : EventArgs
{
    internal CameraFrameEventArgs(CameraFrame frame) =>
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));

    public CameraFrame Frame { get; }
}
