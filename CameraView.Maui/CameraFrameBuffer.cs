namespace CameraView.Maui;

internal readonly record struct CameraFramePlaneDescription(
    int Length,
    int RowStride,
    int PixelStride,
    int Width,
    int Height);

internal abstract class CameraFrameBuffer
{
    private int _references = 1;

    internal abstract int PlaneCount { get; }

    internal virtual byte[] EncodedImage => null;

    internal abstract CameraFramePlaneDescription GetPlaneDescription(int index);

    internal abstract ReadOnlySpan<byte> GetPlaneSpan(int index);

    internal void AddReference()
    {
        while (true)
        {
            var references = Volatile.Read(ref _references);
            if (references <= 0)
                throw new ObjectDisposedException(nameof(CameraFrame));
            if (Interlocked.CompareExchange(
                    ref _references,
                    references + 1,
                    references) == references)
                return;
        }
    }

    internal void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0)
            DisposeCore();
    }

    protected abstract void DisposeCore();
}

internal sealed class ManagedCameraFrameBuffer(byte[] bytes) : CameraFrameBuffer
{
    internal override int PlaneCount => 1;

    internal override byte[] EncodedImage { get; } =
        bytes ?? throw new ArgumentNullException(nameof(bytes));

    internal override CameraFramePlaneDescription GetPlaneDescription(int index)
    {
        if (index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new CameraFramePlaneDescription(
            EncodedImage.Length,
            0,
            0,
            0,
            0);
    }

    internal override ReadOnlySpan<byte> GetPlaneSpan(int index)
    {
        if (index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return EncodedImage;
    }

    protected override void DisposeCore()
    {
    }
}
