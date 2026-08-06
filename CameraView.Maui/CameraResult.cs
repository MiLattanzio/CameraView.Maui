namespace CameraView.Maui;

public sealed class CameraResult : EventArgs
{
    public CameraResult()
    {
        Success = false;
    }

    public CameraResult(
        byte[] image,
        int width = 0,
        int height = 0,
        DateTimeOffset? timestamp = null,
        CameraOrientation orientation = CameraOrientation.Landscape,
        CameraOptions camera = CameraOptions.Rear,
        long sequenceNumber = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        Success = true;
        Image = image;
        Width = width;
        Height = height;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        Orientation = orientation;
        Camera = camera;
        SequenceNumber = sequenceNumber;
    }

    public byte[] Image { get; set; }
    public bool Success { get; set; }
    public int Width { get; }
    public int Height { get; }
    public DateTimeOffset Timestamp { get; }
    public CameraOrientation Orientation { get; }
    public CameraOptions Camera { get; }
    public long SequenceNumber { get; }
}
