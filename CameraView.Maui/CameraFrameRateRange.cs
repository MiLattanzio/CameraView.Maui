namespace CameraView.Maui;

public readonly record struct CameraFrameRateRange
{
    public CameraFrameRateRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || minimum <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        if (!double.IsFinite(maximum) || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public bool IsFixed => Math.Abs(Maximum - Minimum) < 0.0001d;

    public bool Contains(double framesPerSecond) =>
        framesPerSecond >= Minimum && framesPerSecond <= Maximum;

    public override string ToString() => IsFixed
        ? $"{Maximum:0.##} fps"
        : $"{Minimum:0.##}-{Maximum:0.##} fps";
}
