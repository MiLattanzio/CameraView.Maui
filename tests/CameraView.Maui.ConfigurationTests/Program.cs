using CameraView.Maui;

var available = new[]
{
    CameraResolution.Vga,
    new CameraResolution(1024, 768),
    CameraResolution.Hd720p,
    CameraResolution.Hd1080p
};

AssertResolution(
    CameraResolution.Hd720p,
    CameraResolutionSelector.SelectCaptureResolution(
        available,
        CameraCaptureOptions.Default),
    "Default must preserve the 720p-or-lower behavior.");

AssertResolution(
    CameraResolution.Hd720p,
    CameraResolutionSelector.SelectCaptureResolution(
        available,
        new CameraCaptureOptions
        {
            PreferredResolution = new CameraResolution(720, 1280),
            ResolutionSelectionMode = CameraResolutionSelectionMode.Exact
        }),
    "Exact matching must ignore portrait/landscape edge ordering.");

AssertResolution(
    new CameraResolution(1024, 768),
    CameraResolutionSelector.SelectCaptureResolution(
        available,
        new CameraCaptureOptions
        {
            PreferredResolution = new CameraResolution(1600, 1200),
            ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost
        }),
    "AtMost must retain the closest compatible aspect and size.");

AssertResolution(
    new CameraResolution(1024, 768),
    CameraResolutionSelector.SelectCaptureResolution(
        available,
        new CameraCaptureOptions
        {
            PreferredResolution = new CameraResolution(1000, 700),
            ResolutionSelectionMode = CameraResolutionSelectionMode.AtLeast
        }),
    "AtLeast must select the closest size above both requested edges.");

AssertResolution(
    CameraResolution.Vga,
    CameraResolutionSelector.SelectCaptureResolution(
        available,
        new CameraCaptureOptions
        {
            PreferredResolution = new CameraResolution(100, 100),
            ResolutionSelectionMode = CameraResolutionSelectionMode.AtMost
        }),
    "AtMost must fall back to the closest available size when none fit.");

if (CameraResolutionSelector.SelectCaptureResolution(
        available,
        new CameraCaptureOptions
        {
            PreferredResolution = new CameraResolution(1234, 987),
            ResolutionSelectionMode = CameraResolutionSelectionMode.Exact
        }) is not null)
{
    throw new InvalidOperationException("Exact must reject an unavailable size.");
}

AssertResolution(
    new CameraResolution(1024, 768),
    CameraResolutionSelector.SelectPreviewResolution(
        available,
        new CameraResolution(1600, 1200)),
    "Preview selection must account for aspect ratio as well as pixel count.");

AssertResolution(
    CameraResolution.Hd720p,
    CameraResolutionSelector.SelectPreviewResolution(
        available,
        CameraResolution.Hd720p),
    "The stable Android preview target must remain independent from capture profiles.");

var interval = new CameraCaptureOptions
{
    MaximumFrameRate = 10,
    MinimumFrameInterval = TimeSpan.FromMilliseconds(150)
}.GetEffectiveMinimumFrameInterval();
if (interval != TimeSpan.FromMilliseconds(150))
    throw new InvalidOperationException("The strictest delivery interval must win.");

ExpectOutOfRange(
    new CameraCaptureOptions { JpegQuality = 101 },
    "Invalid JPEG quality must be rejected.");
ExpectOutOfRange(
    new CameraCaptureOptions { MaximumFrameRate = double.NaN },
    "Non-finite frame rates must be rejected.");
ExpectOutOfRange(
    new CameraCaptureOptions { MaxOutstandingFrames = 1 },
    "At least two outstanding buffers are required for latest-frame delivery.");
ExpectOutOfRange(
    new CameraCaptureOptions
    {
        FrameRateMode = CameraFrameRateMode.Closest,
        TargetFrameRate = 0
    },
    "Closest native frame-rate selection requires a target.");

if (CameraCaptureOptions.Realtime.FrameFormat != CameraFrameFormat.Native ||
    CameraCaptureOptions.Realtime.FrameRateMode != CameraFrameRateMode.Maximum ||
    CameraCaptureOptions.Realtime.FrameDeliveryMode != CameraFrameDeliveryMode.Latest)
{
    throw new InvalidOperationException(
        "Realtime must select the native, maximum-rate, latest-frame pipeline.");
}

var frameRateRanges = new[]
{
    new CameraFrameRateRange(15, 30),
    new CameraFrameRateRange(30, 60),
    new CameraFrameRateRange(24, 24)
};
AssertFrameRateRange(
    new CameraFrameRateRange(30, 60),
    CameraFrameRateSelector.SelectRange(
        frameRateRanges,
        CameraFrameRateMode.Maximum,
        0),
    "Maximum must select the range with the highest upper bound.");
AssertFrameRateRange(
    new CameraFrameRateRange(24, 24),
    CameraFrameRateSelector.SelectRange(
        frameRateRanges,
        CameraFrameRateMode.Closest,
        24),
    "Closest must prefer the narrowest range containing the target.");

var capabilities = new CameraCaptureCapabilities(
    [CameraFrameFormat.Jpeg, CameraFrameFormat.Yuv420],
    [CameraResolution.Hd720p],
    frameRateRanges);
if (!capabilities.SupportsFrameFormat(CameraFrameFormat.Native) ||
    capabilities.SupportsFrameFormat(CameraFrameFormat.Bgra8888) ||
    !capabilities.SupportsCaptureResolution(new CameraResolution(720, 1280)) ||
    capabilities.MaximumFrameRate != 60)
{
    throw new InvalidOperationException("Camera capability queries are inconsistent.");
}

var controlCapabilities = new CameraControlCapabilities(
    0.5,
    4,
    false,
    true,
    [CameraFocusMode.Continuous],
    -2,
    2,
    0.5);
var requestedControls = new CameraControlOptions
{
    ZoomFactor = 10,
    TorchEnabled = true,
    FocusMode = CameraFocusMode.Single,
    FocusPoint = new CameraPoint(0.25, 0.75),
    ExposureCompensation = 1.3
};
var effectiveControls = CameraControlNegotiator.Negotiate(
    requestedControls,
    controlCapabilities,
    CameraOptions.Rear);
if (!controlCapabilities.IsZoomSupported)
    throw new InvalidOperationException("A non-degenerate native zoom range must report zoom support.");
AssertCloseDouble(4, effectiveControls.ZoomFactor, "Zoom must be clamped to the device maximum.");
AssertCloseDouble(1.5, effectiveControls.ExposureCompensation, "Exposure must be quantized to the native step.");
if (effectiveControls.TorchEnabled ||
    effectiveControls.FocusMode != CameraFocusMode.Continuous ||
    effectiveControls.FocusPoint != requestedControls.FocusPoint ||
    effectiveControls.IsPreviewMirrored ||
    !effectiveControls.UsedZoomFallback ||
    !effectiveControls.UsedTorchFallback ||
    !effectiveControls.UsedFocusFallback ||
    !effectiveControls.UsedExposureFallback)
{
    throw new InvalidOperationException("Camera control fallback reporting is inconsistent.");
}

var frontControls = CameraControlNegotiator.Negotiate(
    CameraControlOptions.Default,
    controlCapabilities,
    CameraOptions.Front);
if (!frontControls.IsPreviewMirrored)
    throw new InvalidOperationException("Automatic preview mirroring must mirror the front camera.");
var unmirroredFrontControls = CameraControlNegotiator.Negotiate(
    CameraControlOptions.Default with
    {
        PreviewMirroring = CameraPreviewMirroringMode.Unmirrored
    },
    controlCapabilities,
    CameraOptions.Front);
if (unmirroredFrontControls.IsPreviewMirrored)
    throw new InvalidOperationException("Explicit unmirrored preview mode must override the front-camera default.");

var mappedPoint = CameraControlPointMapper.ToSensorPoint(
    new CameraPoint(0.2, 0.3),
    90,
    false);
AssertCloseDouble(0.3, mappedPoint.X, "A 90-degree preview point must map X to sensor Y.");
AssertCloseDouble(0.8, mappedPoint.Y, "A 90-degree preview point must invert sensor Y.");
var mirroredPoint = CameraControlPointMapper.ToSensorPoint(
    new CameraPoint(0.2, 0.3),
    0,
    true);
AssertCloseDouble(0.8, mirroredPoint.X, "Preview mirroring must be undone before sensor mapping.");
var croppedPoint = CameraControlPointMapper.ToSensorPoint(
    new CameraPoint(0, 0.5),
    1000,
    1000,
    1920,
    1080,
    0,
    false);
AssertCloseDouble(0.21875, croppedPoint.X, "Aspect-fill cropping must be included in focus-point mapping.");
AssertCloseDouble(0.5, croppedPoint.Y, "Centered aspect-fill coordinates must remain centered.");

ExpectControlOutOfRange(
    new CameraControlOptions { ZoomFactor = 0 },
    "Non-positive zoom must be rejected.");
ExpectControlOutOfRange(
    new CameraControlOptions { ExposureCompensation = double.NaN },
    "Non-finite exposure compensation must be rejected.");

var trackedBuffer = new TrackingFrameBuffer([1, 2, 3, 4]);
var borrowedFrame = new CameraFrame(
    trackedBuffer,
    CameraFrameFormat.Jpeg,
    2,
    2,
    DateTimeOffset.UtcNow,
    CameraOrientation.Portrait,
    CameraOptions.Rear,
    42,
    null,
    90,
    false);
var retainedFrame = borrowedFrame.Retain();
borrowedFrame.Dispose();
if (trackedBuffer.DisposeCount != 0 ||
    retainedFrame.Planes[0].Span[2] != 3 ||
    retainedFrame.SequenceNumber != 42 ||
    retainedFrame.RotationDegrees != 90)
{
    throw new InvalidOperationException("A retained frame must keep its shared buffer alive.");
}
retainedFrame.Dispose();
if (trackedBuffer.DisposeCount != 1)
    throw new InvalidOperationException("The final frame lease must release its buffer once.");
try
{
    _ = retainedFrame.Retain();
    throw new InvalidOperationException("A disposed frame must not be retainable.");
}
catch (ObjectDisposedException)
{
}

AssertEqual(
    90,
    CameraPreviewTransformCalculator.ComputeRelativeRotation(90, 0, false),
    "A rear sensor mounted at 90 degrees must swap axes in natural orientation.");
AssertEqual(
    180,
    CameraPreviewTransformCalculator.ComputeRelativeRotation(90, 90, false),
    "Rear-camera relative rotation must include display rotation.");
AssertEqual(
    180,
    CameraPreviewTransformCalculator.ComputeRelativeRotation(270, 90, true),
    "Front-camera relative rotation must use the opposite display sign.");

var portraitTransform = CameraPreviewTransformCalculator.Calculate(
    1080,
    1800,
    1920,
    1080,
    90,
    0,
    false);
AssertEqual(1080, portraitTransform.Width, "Portrait preview width is incorrect.");
AssertEqual(1920, portraitTransform.Height, "Portrait preview height is incorrect.");

var landscapeTransform = CameraPreviewTransformCalculator.Calculate(
    1800,
    1080,
    1920,
    1080,
    90,
    90,
    false);
AssertEqual(1920, landscapeTransform.Width, "Landscape preview width is incorrect.");
AssertEqual(1080, landscapeTransform.Height, "Landscape preview height is incorrect.");

var mirroredTransform = CameraPreviewTransformCalculator.Calculate(
    1080,
    1800,
    1920,
    1080,
    90,
    0,
    true,
    true);
if (!mirroredTransform.IsMirrored || mirroredTransform.Width <= 0)
    throw new InvalidOperationException("A mirrored preview must preserve layout and request a final horizontal flip.");

var standardTransform = CameraPreviewTransformCalculator.Calculate(
    1080,
    1800,
    1600,
    1200,
    90,
    0,
    false);
var widescreenTransformAfterProfileChange = CameraPreviewTransformCalculator.Calculate(
    1080,
    1800,
    1920,
    1080,
    90,
    0,
    false);
if (standardTransform == portraitTransform)
    throw new InvalidOperationException("A 4:3 preview must not reuse the 16:9 preview transform.");
if (widescreenTransformAfterProfileChange != portraitTransform)
    throw new InvalidOperationException("Returning to a preview profile must restore its original transform.");

Console.WriteLine("Validated capture options, interactive controls, frame rates, resolution negotiation, and preview transforms.");

static void AssertResolution(
    CameraResolution expected,
    CameraResolution? actual,
    string message)
{
    if (actual is null || !expected.HasSameDimensions(actual.Value))
    {
        throw new InvalidOperationException(
            $"{message} Expected {expected}, actual {actual?.ToString() ?? "null"}.");
    }
}

static void ExpectOutOfRange(CameraCaptureOptions options, string message)
{
    try
    {
        options.Validate();
    }
    catch (ArgumentOutOfRangeException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void ExpectControlOutOfRange(CameraControlOptions options, string message)
{
    try
    {
        options.Validate();
    }
    catch (ArgumentOutOfRangeException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void AssertEqual(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException($"{message} Expected {expected}, actual {actual}.");
}

static void AssertFrameRateRange(
    CameraFrameRateRange expected,
    CameraFrameRateRange? actual,
    string message)
{
    if (actual != expected)
    {
        throw new InvalidOperationException(
            $"{message} Expected {expected}, actual {actual?.ToString() ?? "null"}.");
    }
}

static void AssertCloseDouble(double expected, double actual, string message)
{
    if (Math.Abs(expected - actual) > 0.0001)
    {
        throw new InvalidOperationException(
            $"{message} Expected {expected}, actual {actual}.");
    }
}

sealed class TrackingFrameBuffer(byte[] bytes) : CameraFrameBuffer
{
    internal int DisposeCount { get; private set; }

    internal override int PlaneCount => 1;

    internal override CameraFramePlaneDescription GetPlaneDescription(int index) =>
        index == 0
            ? new CameraFramePlaneDescription(bytes.Length, bytes.Length, 1, bytes.Length, 1)
            : throw new ArgumentOutOfRangeException(nameof(index));

    internal override ReadOnlySpan<byte> GetPlaneSpan(int index) =>
        index == 0 ? bytes : throw new ArgumentOutOfRangeException(nameof(index));

    protected override void DisposeCore() => DisposeCount++;
}
