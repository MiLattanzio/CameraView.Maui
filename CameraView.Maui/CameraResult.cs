namespace CameraView.Maui;

public sealed class CameraResult : EventArgs
{
    public CameraResult()
    {
        Success = false;
    }

    public CameraResult(byte[] image)
        : this(
            image,
            0,
            0,
            DateTimeOffset.UtcNow,
            CameraOrientation.Landscape,
            CameraOptions.Rear,
            0,
            null)
    {
    }

    internal CameraResult(
        byte[] image,
        int width,
        int height,
        DateTimeOffset timestamp,
        CameraOrientation orientation,
        CameraOptions camera,
        long sequenceNumber,
        CameraCaptureConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(image);
        Success = true;
        Image = image;
        Width = width;
        Height = height;
        Timestamp = timestamp;
        Orientation = orientation;
        Camera = camera;
        SequenceNumber = sequenceNumber;
        Configuration = configuration;
    }

    public byte[] Image { get; set; }

    public bool Success { get; set; }

    public int Width { get; }

    public int Height { get; }

    public DateTimeOffset Timestamp { get; }

    public CameraOrientation Orientation { get; }

    public CameraOptions Camera { get; }

    public long SequenceNumber { get; }

    public CameraCaptureConfiguration Configuration { get; }
}
