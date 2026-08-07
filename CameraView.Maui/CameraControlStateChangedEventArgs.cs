namespace CameraView.Maui;

public sealed class CameraControlStateChangedEventArgs : EventArgs
{
    public CameraControlStateChangedEventArgs(
        CameraControlState previousState,
        CameraControlState state)
    {
        PreviousState = previousState;
        State = state;
    }

    public CameraControlState PreviousState { get; }

    public CameraControlState State { get; }
}
