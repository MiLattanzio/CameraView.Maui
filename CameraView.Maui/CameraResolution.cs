namespace CameraView.Maui;

public readonly struct CameraResolution : IEquatable<CameraResolution>
{
    public static CameraResolution Default => default;

    public static CameraResolution Qvga => new(320, 240);

    public static CameraResolution Vga => new(640, 480);

    public static CameraResolution Hd720p => new(1280, 720);

    public static CameraResolution Hd1080p => new(1920, 1080);

    public static CameraResolution Uhd4K => new(3840, 2160);

    public CameraResolution(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public bool IsDefault => Width == 0 && Height == 0;

    public long PixelCount => (long)Width * Height;

    public bool HasSameDimensions(CameraResolution other) =>
        Math.Max(Width, Height) == Math.Max(other.Width, other.Height) &&
        Math.Min(Width, Height) == Math.Min(other.Width, other.Height);

    public bool Equals(CameraResolution other) =>
        Width == other.Width && Height == other.Height;

    public override bool Equals(object obj) =>
        obj is CameraResolution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Width, Height);

    public override string ToString() => IsDefault ? "Default" : $"{Width}x{Height}";

    public static bool operator ==(CameraResolution left, CameraResolution right) =>
        left.Equals(right);

    public static bool operator !=(CameraResolution left, CameraResolution right) =>
        !left.Equals(right);
}
