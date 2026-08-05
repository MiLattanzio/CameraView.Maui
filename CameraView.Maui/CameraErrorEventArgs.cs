namespace CameraView.Maui;

public sealed class CameraErrorEventArgs : EventArgs
{
    internal CameraErrorEventArgs(
        CameraErrorCode code,
        string message,
        CameraOptions camera,
        bool isRecoverable,
        string platformCode,
        Exception exception)
    {
        Code = code;
        Message = message;
        Camera = camera;
        IsRecoverable = isRecoverable;
        PlatformCode = platformCode;
        Exception = exception;
    }

    public CameraErrorCode Code { get; }

    public string Message { get; }

    public CameraOptions Camera { get; }

    public bool IsRecoverable { get; }

    public string PlatformCode { get; }

    public Exception Exception { get; }
}
