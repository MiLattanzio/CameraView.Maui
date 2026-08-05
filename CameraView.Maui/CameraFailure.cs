namespace CameraView.Maui;

internal sealed class CameraFailure
{
    public CameraFailure(
        CameraErrorCode code,
        string message,
        bool isRecoverable,
        string platformCode = null,
        Exception exception = null)
    {
        Code = code;
        Message = message;
        IsRecoverable = isRecoverable;
        PlatformCode = platformCode;
        Exception = exception;
    }

    public CameraErrorCode Code { get; }

    public string Message { get; }

    public bool IsRecoverable { get; }

    public string PlatformCode { get; }

    public Exception Exception { get; }
}

internal sealed class CameraPlatformException : Exception
{
    public CameraPlatformException(CameraFailure failure)
        : base(failure.Message, failure.Exception)
    {
        Failure = failure;
    }

    public CameraFailure Failure { get; }
}
