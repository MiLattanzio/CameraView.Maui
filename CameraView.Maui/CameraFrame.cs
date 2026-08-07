namespace CameraView.Maui;

public sealed class CameraFrame : IDisposable
{
    private CameraFrameBuffer _buffer;
    private readonly CameraFramePlane[] _planes;

    internal CameraFrame(
        CameraFrameBuffer buffer,
        CameraFrameFormat format,
        int width,
        int height,
        DateTimeOffset timestamp,
        CameraOrientation orientation,
        CameraOptions camera,
        long sequenceNumber,
        CameraCaptureConfiguration configuration,
        int rotationDegrees,
        bool isMirrored)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Format = format;
        Width = width;
        Height = height;
        Timestamp = timestamp;
        Orientation = orientation;
        Camera = camera;
        SequenceNumber = sequenceNumber;
        Configuration = configuration;
        RotationDegrees = rotationDegrees;
        IsMirrored = isMirrored;
        _planes = Enumerable.Range(0, buffer.PlaneCount)
            .Select(index => new CameraFramePlane(
                this,
                index,
                buffer.GetPlaneDescription(index)))
            .ToArray();
    }

    ~CameraFrame() => Dispose(false);

    public CameraFrameFormat Format { get; }

    public int Width { get; }

    public int Height { get; }

    public DateTimeOffset Timestamp { get; }

    public CameraOrientation Orientation { get; }

    public CameraOptions Camera { get; }

    public long SequenceNumber { get; }

    public CameraCaptureConfiguration Configuration { get; }

    public int RotationDegrees { get; }

    public bool IsMirrored { get; }

    public IReadOnlyList<CameraFramePlane> Planes => _planes;

    public bool IsDisposed => Volatile.Read(ref _buffer) is null;

    public CameraFrame Retain()
    {
        var buffer = GetBuffer();
        buffer.AddReference();
        return new CameraFrame(
            buffer,
            Format,
            Width,
            Height,
            Timestamp,
            Orientation,
            Camera,
            SequenceNumber,
            Configuration,
            RotationDegrees,
            IsMirrored);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal ReadOnlySpan<byte> GetPlaneSpan(int index) =>
        GetBuffer().GetPlaneSpan(index);

    private CameraFrameBuffer GetBuffer() =>
        Volatile.Read(ref _buffer) ??
        throw new ObjectDisposedException(nameof(CameraFrame));

    private void Dispose(bool disposing) =>
        Interlocked.Exchange(ref _buffer, null)?.Release();
}
