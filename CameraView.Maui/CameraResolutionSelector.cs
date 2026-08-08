namespace CameraView.Maui;

internal static class CameraResolutionSelector
{
    public static CameraResolution? SelectCaptureResolution(
        IEnumerable<CameraResolution> availableResolutions,
        CameraCaptureOptions options)
    {
        var available = availableResolutions
            .Where(resolution => !resolution.IsDefault)
            .Distinct()
            .ToArray();
        if (available.Length == 0)
            return null;

        var requested = options.PreferredResolution;
        if (requested.IsDefault)
        {
            var legacy = available
                .Where(resolution =>
                    LongEdge(resolution) <= CameraResolution.Hd720p.Width &&
                    ShortEdge(resolution) <= CameraResolution.Hd720p.Height)
                .ToArray();
            return legacy.Length > 0
                ? legacy
                    .OrderBy(resolution => SelectionScore(
                        resolution,
                        CameraResolution.Hd720p))
                    .First()
                : available.OrderBy(resolution => resolution.PixelCount).First();
        }

        var exact = available.FirstOrDefault(resolution => SameDimensions(resolution, requested));
        if (!exact.IsDefault)
            return exact;
        if (options.ResolutionSelectionMode == CameraResolutionSelectionMode.Exact)
            return null;

        var constrained = options.ResolutionSelectionMode switch
        {
            CameraResolutionSelectionMode.AtMost => available
                .Where(resolution =>
                    LongEdge(resolution) <= LongEdge(requested) &&
                    ShortEdge(resolution) <= ShortEdge(requested))
                .ToArray(),
            CameraResolutionSelectionMode.AtLeast => available
                .Where(resolution =>
                    LongEdge(resolution) >= LongEdge(requested) &&
                    ShortEdge(resolution) >= ShortEdge(requested))
                .ToArray(),
            _ => available
        };

        if (constrained.Length == 0)
            constrained = available;

        return options.ResolutionSelectionMode switch
        {
            CameraResolutionSelectionMode.AtMost => constrained
                .OrderBy(resolution => SelectionScore(resolution, requested))
                .First(),
            CameraResolutionSelectionMode.AtLeast => constrained
                .OrderBy(resolution => SelectionScore(resolution, requested))
                .First(),
            _ => constrained
                .OrderBy(resolution => SelectionScore(resolution, requested))
                .ThenByDescending(resolution => resolution.PixelCount)
                .First()
        };
    }

    public static CameraResolution? SelectPreviewResolution(
        IEnumerable<CameraResolution> availableResolutions,
        CameraResolution previewTarget)
    {
        var available = availableResolutions
            .Where(resolution => !resolution.IsDefault)
            .Distinct()
            .ToArray();
        if (available.Length == 0)
            return null;

        return available
            .OrderBy(resolution => SelectionScore(resolution, previewTarget))
            .ThenByDescending(resolution => resolution.PixelCount)
            .First();
    }

    private static double SelectionScore(
        CameraResolution candidate,
        CameraResolution requested)
    {
        var aspectError = AspectRatioError(candidate, requested);
        var areaError = Math.Abs(Math.Log(
            candidate.PixelCount / (double)requested.PixelCount));
        return aspectError * 4d + areaError;
    }

    private static double AspectRatioError(
        CameraResolution candidate,
        CameraResolution requested) =>
        Math.Abs(Math.Log(AspectRatio(candidate) / AspectRatio(requested)));

    private static double AspectRatio(CameraResolution resolution) =>
        LongEdge(resolution) / (double)ShortEdge(resolution);

    private static bool SameDimensions(CameraResolution left, CameraResolution right) =>
        left.HasSameDimensions(right);

    private static int LongEdge(CameraResolution resolution) =>
        Math.Max(resolution.Width, resolution.Height);

    private static int ShortEdge(CameraResolution resolution) =>
        Math.Min(resolution.Width, resolution.Height);
}
