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
AssertClose(1f, portraitTransform.ScaleX, "Portrait X correction is incorrect.");
AssertClose(16f / 15f, portraitTransform.ScaleY, "Portrait Y correction is incorrect.");
AssertClose(0f, portraitTransform.RotationDegrees, "Natural orientation must not add display rotation.");

var landscapeTransform = CameraPreviewTransformCalculator.Calculate(
    1800,
    1080,
    1920,
    1080,
    90,
    90,
    false);
AssertClose(3f / 5f, landscapeTransform.ScaleX, "Landscape X correction is incorrect.");
AssertClose(16f / 9f, landscapeTransform.ScaleY, "Landscape Y correction is incorrect.");
AssertClose(-90f, landscapeTransform.RotationDegrees, "Landscape display rotation is incorrect.");

Console.WriteLine("Validated capture options, frame rates, resolution negotiation, and preview transforms.");

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

static void AssertClose(float expected, float actual, string message)
{
    if (Math.Abs(expected - actual) > 0.0001f)
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
