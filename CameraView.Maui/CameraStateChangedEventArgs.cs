namespace CameraView.Maui;

public sealed class CameraStateChangedEventArgs : EventArgs
{
    internal CameraStateChangedEventArgs(
        CameraState previousState,
        CameraState state,
        CameraOptions camera)
    {
        PreviousState = previousState;
        State = state;
        Camera = camera;
    }

    public CameraState PreviousState { get; }

    public CameraState State { get; }

    public CameraOptions Camera { get; }
}
