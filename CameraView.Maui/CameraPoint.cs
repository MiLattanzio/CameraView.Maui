namespace CameraView.Maui;

public readonly record struct CameraPoint
{
    public CameraPoint(double x, double y)
    {
        if (!double.IsFinite(x) || x is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "The normalized X coordinate must be between 0 and 1.");
        if (!double.IsFinite(y) || y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(y), "The normalized Y coordinate must be between 0 and 1.");

        X = x;
        Y = y;
    }

    public static CameraPoint Center { get; } = new(0.5, 0.5);

    public double X { get; }

    public double Y { get; }

    public override string ToString() => $"{X:0.###}, {Y:0.###}";
}
