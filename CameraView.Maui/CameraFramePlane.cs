namespace CameraView.Maui;

public readonly struct CameraFramePlane
{
    private readonly CameraFrame _frame;
    private readonly int _index;

    internal CameraFramePlane(
        CameraFrame frame,
        int index,
        CameraFramePlaneDescription description)
    {
        _frame = frame;
        _index = index;
        Length = description.Length;
        RowStride = description.RowStride;
        PixelStride = description.PixelStride;
        Width = description.Width;
        Height = description.Height;
    }

    public int Length { get; }

    public int RowStride { get; }

    public int PixelStride { get; }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<byte> Span => _frame is null
        ? ReadOnlySpan<byte>.Empty
        : _frame.GetPlaneSpan(_index);

    public void CopyTo(Span<byte> destination) => Span.CopyTo(destination);

    public byte[] ToArray() => Span.ToArray();
}
