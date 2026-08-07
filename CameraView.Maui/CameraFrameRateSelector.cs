namespace CameraView.Maui;

internal static class CameraFrameRateSelector
{
    internal static CameraFrameRateRange? SelectRange(
        IEnumerable<CameraFrameRateRange> ranges,
        CameraFrameRateMode mode,
        double targetFrameRate)
    {
        if (mode == CameraFrameRateMode.PlatformDefault)
            return null;

        var available = ranges?.Distinct().ToArray() ?? [];
        if (available.Length == 0)
            return null;

        if (mode == CameraFrameRateMode.Maximum)
        {
            return available
                .OrderByDescending(range => range.Maximum)
                .ThenByDescending(range => range.Minimum)
                .First();
        }

        return available
            .OrderBy(range => DistanceToRange(targetFrameRate, range))
            .ThenBy(range => Math.Abs(range.Maximum - range.Minimum))
            .ThenByDescending(range => range.Maximum)
            .First();
    }

    internal static double SelectFrameRate(
        CameraFrameRateRange range,
        CameraFrameRateMode mode,
        double targetFrameRate) =>
        mode == CameraFrameRateMode.Maximum
            ? range.Maximum
            : Math.Clamp(targetFrameRate, range.Minimum, range.Maximum);

    private static double DistanceToRange(
        double targetFrameRate,
        CameraFrameRateRange range)
    {
        if (range.Contains(targetFrameRate))
            return 0;
        return targetFrameRate < range.Minimum
            ? range.Minimum - targetFrameRate
            : targetFrameRate - range.Maximum;
    }
}
