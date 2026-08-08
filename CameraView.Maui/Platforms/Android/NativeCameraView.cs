using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Java.Lang;
using Math = System.Math;
using Rect = Android.Graphics.Rect;
using Size = Android.Util.Size;

namespace CameraView.Maui;

public sealed class NativeCameraView : FrameLayout
{
    private readonly FrameLayout _previewHost;
    private readonly SurfaceView _surfaceView;
    private readonly CameraSurfaceHolderCallback _surfaceHolderCallback;
    private readonly CameraStateListener _cameraStateListener;
    private readonly ImageAvailableListener _imageAvailableListener;
    private readonly DisplayRotationListener _displayRotationListener;
    private readonly Handler _mainHandler;

    private CameraManager _cameraManager;
    private DisplayManager _displayManager;
    private CameraDevice _cameraDevice;
    private CameraCaptureSession _previewSession;
    private CaptureRequest.Builder _previewBuilder;
    private ImageReader _imageReader;
    private Surface _previewSurface;
    private HandlerThread _backgroundThread;
    private Handler _backgroundHandler;
    private SessionConfiguration _sessionConfiguration;
    private List<OutputConfiguration> _outputConfigurations;
    private Size _previewSize;
    private CameraCharacteristics _cameraCharacteristics;
    private Action<CameraFrameBuffer, CameraFrameFormat, int, int, DateTimeOffset, CameraCaptureConfiguration, int, bool> _frameCaptured;
    private Action<CameraCaptureConfiguration> _configurationSelected;
    private Action<CameraControlState> _controlsSelected;
    private Action _captureStarted;
    private Action _captureSuspended;
    private Action<CameraFailure> _captureFailed;
    private CameraOptions _cameraOption;
    private CameraOrientation _orientation;
    private CameraCaptureOptions _captureOptions;
    private CameraControlOptions _controlOptions;
    private CameraResolution _captureResolution;
    private CameraCaptureConfiguration _effectiveConfiguration;
    private bool _isRunning;
    private int? _jpegQuality;
    private TimeSpan _minimumFrameInterval;
    private long _lastFrameTicks;
    private CameraFrameFormat _frameFormat;
    private Android.Util.Range _nativeFrameRateRange;
    private CameraFrameRateRange? _effectiveNativeFrameRate;
    private CameraCaptureCapabilities _capabilities;
    private CameraControlCapabilities _controlCapabilities;
    private CameraControlState _effectiveControls;
    private CameraFrameRateRange[] _availableFrameRateRanges = [];

    public NativeCameraView(Context context) : base(context)
    {
        _previewHost = new FrameLayout(context);
        _previewHost.SetClipChildren(true);
        _previewHost.SetClipToPadding(true);
        _surfaceView = new SurfaceView(context);
        _surfaceHolderCallback = new CameraSurfaceHolderCallback(this);
        _cameraStateListener = new CameraStateListener(this);
        _imageAvailableListener = new ImageAvailableListener(this);
        _displayRotationListener = new DisplayRotationListener(this);
        _mainHandler = new Handler(Looper.MainLooper);
        _surfaceView.Holder.AddCallback(_surfaceHolderCallback);

        var hostLayout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent)
        {
            Gravity = GravityFlags.Center
        };
        AddView(_previewHost, hostLayout);
    }

    public void Start(
        CameraOptions cameraOption,
        CameraOrientation orientation,
        Action<byte[]> frameCaptured) =>
        Start(
            cameraOption,
            orientation,
            (buffer, _, _, _, _, _, _, _) =>
            {
                try
                {
                    var bytes = buffer.EncodedImage;
                    if (bytes is { Length: > 0 })
                        frameCaptured(bytes);
                }
                finally
                {
                    buffer.Release();
                }
            },
            CameraCaptureOptions.Default,
            CameraControlOptions.Default,
            null,
            null,
            null,
            null,
            null);

    internal void Start(
        CameraOptions cameraOption,
        CameraOrientation orientation,
        Action<CameraFrameBuffer, CameraFrameFormat, int, int, DateTimeOffset, CameraCaptureConfiguration, int, bool> frameCaptured,
        CameraCaptureOptions captureOptions,
        CameraControlOptions controlOptions,
        Action<CameraCaptureConfiguration> configurationSelected,
        Action<CameraControlState> controlsSelected,
        Action captureStarted,
        Action captureSuspended,
        Action<CameraFailure> captureFailed)
    {
        if (_isRunning &&
            _cameraOption == cameraOption &&
            _orientation == orientation &&
            Equals(_captureOptions, captureOptions))
        {
            _frameCaptured = frameCaptured;
            _captureStarted = captureStarted;
            _captureSuspended = captureSuspended;
            _captureFailed = captureFailed;
            _configurationSelected = configurationSelected;
            UpdateControls(controlOptions, controlsSelected, captureFailed);
            return;
        }

        Stop();

        _cameraOption = cameraOption;
        _orientation = orientation;
        _captureOptions = captureOptions;
        _controlOptions = controlOptions;
        _frameCaptured = frameCaptured;
        _captureStarted = captureStarted;
        _captureSuspended = captureSuspended;
        _captureFailed = captureFailed;
        _configurationSelected = configurationSelected;
        _controlsSelected = controlsSelected;
        _jpegQuality = captureOptions.JpegQuality;
        _minimumFrameInterval = captureOptions.GetEffectiveMinimumFrameInterval();
        _lastFrameTicks = 0;

        try
        {
            _cameraManager = Context.GetSystemService(Context.CameraService) as CameraManager
                ?? throw new CameraPlatformException(new CameraFailure(
                    CameraErrorCode.CameraUnavailable,
                    "The Android camera service is unavailable.",
                    true,
                    "CameraServiceUnavailable"));

            StartBackgroundThread();

            var cameraId = FindCameraId(cameraOption);
            _cameraCharacteristics = _cameraManager.GetCameraCharacteristics(cameraId);
            _controlCapabilities = GetControlCapabilities(_cameraCharacteristics);
            _effectiveControls = CameraControlNegotiator.Negotiate(
                _controlOptions,
                _controlCapabilities,
                _cameraOption);
            var configurationMap = _cameraCharacteristics.Get(
                CameraCharacteristics.ScalerStreamConfigurationMap) as StreamConfigurationMap
                ?? throw new CameraPlatformException(new CameraFailure(
                    CameraErrorCode.SessionConfigurationFailed,
                    "Camera stream configuration is unavailable.",
                    true,
                    "MissingStreamConfiguration"));

            var imageFormat = ResolveImageFormat(captureOptions.FrameFormat, out _frameFormat);
            var captureSize = SelectCaptureSize(
                configurationMap.GetOutputSizes((int)imageFormat),
                captureOptions);
            _captureResolution = ToResolution(captureSize);
            _previewSize = SelectPreviewSize(
                configurationMap.GetOutputSizes(Class.FromType(typeof(SurfaceTexture))),
                CameraResolution.Hd720p);
            _imageReader = ImageReader.NewInstance(
                captureSize.Width,
                captureSize.Height,
                imageFormat,
                captureOptions.MaxOutstandingFrames);
            _imageReader.SetOnImageAvailableListener(_imageAvailableListener, _backgroundHandler);
            SelectNativeFrameRate();
            _capabilities = new CameraCaptureCapabilities(
                GetSupportedFrameFormats(configurationMap),
                configurationMap.GetOutputSizes((int)imageFormat)
                    .Select(ToResolution),
                _availableFrameRateRanges);

            _isRunning = true;
            ConfigureTransform(Width, Height);
            _cameraManager.OpenCamera(cameraId, _cameraStateListener, _backgroundHandler);
        }
        catch (CameraPlatformException)
        {
            Stop();
            throw;
        }
        catch (SecurityException exception)
        {
            Stop();
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.PermissionDenied,
                "Android denied access to the camera.",
                true,
                exception.GetType().Name,
                exception));
        }
        catch (CameraAccessException exception)
        {
            Stop();
            throw new CameraPlatformException(MapCameraAccessException(exception));
        }
        catch (System.Exception exception)
        {
            Stop();
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.Unknown,
                "Android could not start the camera.",
                true,
                exception.GetType().Name,
                exception));
        }
    }

    internal void UpdateControls(
        CameraControlOptions controlOptions,
        Action<CameraControlState> controlsSelected,
        Action<CameraFailure> controlsFailed)
    {
        ArgumentNullException.ThrowIfNull(controlOptions);
        controlOptions.Validate();
        _controlOptions = controlOptions;
        _controlsSelected = controlsSelected;

        if (!_isRunning || _controlCapabilities is null)
            return;

        var previousControls = _effectiveControls;
        try
        {
            var effectiveControls = CameraControlNegotiator.Negotiate(
                controlOptions,
                _controlCapabilities,
                _cameraOption);
            var focusChanged = previousControls?.FocusMode != effectiveControls.FocusMode ||
                               previousControls?.FocusPoint != effectiveControls.FocusPoint;
            _effectiveControls = effectiveControls;
            ConfigureTransform(Width, Height);
            if (_previewBuilder is not null && _previewSession is not null)
            {
                ApplyControlsToBuilder(_previewBuilder);
                SubmitControlRequest(focusChanged);
                _controlsSelected?.Invoke(_effectiveControls);
            }
        }
        catch (CameraAccessException exception)
        {
            _effectiveControls = previousControls;
            ConfigureTransform(Width, Height);
            controlsFailed?.Invoke(new CameraFailure(
                CameraErrorCode.ControlConfigurationFailed,
                "Android could not apply the requested camera controls.",
                true,
                exception.Reason.ToString(),
                exception));
        }
        catch (System.Exception exception)
        {
            _effectiveControls = previousControls;
            ConfigureTransform(Width, Height);
            controlsFailed?.Invoke(new CameraFailure(
                CameraErrorCode.ControlConfigurationFailed,
                "Android could not apply the requested camera controls.",
                true,
                exception.GetType().Name,
                exception));
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _frameCaptured = null;
        _captureStarted = null;
        _captureSuspended = null;
        _captureFailed = null;
        _configurationSelected = null;
        _controlsSelected = null;
        _captureOptions = null;
        _controlOptions = null;
        _effectiveConfiguration = null;
        _captureResolution = CameraResolution.Default;
        _frameFormat = CameraFrameFormat.Jpeg;
        _nativeFrameRateRange?.Dispose();
        _nativeFrameRateRange = null;
        _effectiveNativeFrameRate = null;
        _capabilities = null;
        _controlCapabilities = null;
        _effectiveControls = null;
        _availableFrameRateRanges = [];
        _lastFrameTicks = 0;

        if (_previewSession is not null)
        {
            try
            {
                _previewSession.StopRepeating();
                _previewSession.AbortCaptures();
            }
            catch (CameraAccessException)
            {
                // The device may already have been disconnected.
            }
            catch (IllegalStateException)
            {
                // The capture session may already be closed.
            }

            _previewSession.Close();
            _previewSession.Dispose();
            _previewSession = null;
        }

        _previewBuilder?.Dispose();
        _previewBuilder = null;
        DisposeSessionConfiguration();

        _cameraDevice?.Close();
        _cameraDevice?.Dispose();
        _cameraDevice = null;

        _imageReader?.Close();
        _imageReader?.Dispose();
        _imageReader = null;

        _previewSurface = null;
        _cameraCharacteristics = null;
        _previewSize = null;
        StopBackgroundThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnregisterDisplayListener();
            Stop();
            _mainHandler.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        RegisterDisplayListener();
        ConfigureTransform(Width, Height);
    }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        ConfigureTransform(width, height);
    }

    protected override void OnDetachedFromWindow()
    {
        UnregisterDisplayListener();
        base.OnDetachedFromWindow();
    }

    private string FindCameraId(CameraOptions cameraOption)
    {
        var requestedFacing = cameraOption == CameraOptions.Front
            ? LensFacing.Front
            : LensFacing.Back;

        foreach (var cameraId in _cameraManager.GetCameraIdList())
        {
            var characteristics = _cameraManager.GetCameraCharacteristics(cameraId);
            var facing = characteristics.Get(CameraCharacteristics.LensFacing) as Integer;
            if (facing?.IntValue() == (int)requestedFacing)
                return cameraId;
        }

        throw new CameraPlatformException(new CameraFailure(
            CameraErrorCode.CameraUnavailable,
            $"No {cameraOption} camera was found.",
            false,
            "CameraNotFound"));
    }

    private static Size SelectCaptureSize(
        IEnumerable<Size> sizes,
        CameraCaptureOptions options)
    {
        var available = RequireSizes(sizes);
        var selected = CameraResolutionSelector.SelectCaptureResolution(
            available.Select(ToResolution),
            options);
        if (selected is null)
        {
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                $"The requested exact resolution {options.PreferredResolution} is unavailable.",
                false,
                "ExactResolutionUnavailable"));
        }

        return available.First(size => ToResolution(size) == selected.Value);
    }

    private static ImageFormatType ResolveImageFormat(
        CameraFrameFormat requestedFormat,
        out CameraFrameFormat effectiveFormat)
    {
        switch (requestedFormat)
        {
            case CameraFrameFormat.Jpeg:
                effectiveFormat = CameraFrameFormat.Jpeg;
                return ImageFormatType.Jpeg;
            case CameraFrameFormat.Native:
            case CameraFrameFormat.Yuv420:
                effectiveFormat = CameraFrameFormat.Yuv420;
                return ImageFormatType.Yuv420888;
            case CameraFrameFormat.Bgra8888:
                throw new CameraPlatformException(new CameraFailure(
                    CameraErrorCode.SessionConfigurationFailed,
                    "Android Camera2 does not expose a portable BGRA ImageReader output. Use Native or Yuv420.",
                    false,
                    "UnsupportedFrameFormat"));
            default:
                throw new ArgumentOutOfRangeException(nameof(requestedFormat));
        }
    }

    private void SelectNativeFrameRate()
    {
        _nativeFrameRateRange?.Dispose();
        _nativeFrameRateRange = null;
        _effectiveNativeFrameRate = null;

        var rangesObject = _cameraCharacteristics.Get(
            CameraCharacteristics.ControlAeAvailableTargetFpsRanges);
#pragma warning disable CA1422
        var ranges = rangesObject is null
            ? []
            : JNIEnv.GetArray<Android.Util.Range>(rangesObject.Handle);
#pragma warning restore CA1422
        var candidates = ranges?
            .Select(range => new
            {
                Native = range,
                Common = ToFrameRateRange(range)
            })
            .Where(candidate => candidate.Common.HasValue)
            .ToArray() ?? [];
        _availableFrameRateRanges = candidates
            .Select(candidate => candidate.Common!.Value)
            .Distinct()
            .ToArray();
        if (_captureOptions.FrameRateMode == CameraFrameRateMode.PlatformDefault)
        {
            foreach (var candidate in candidates)
                candidate.Native.Dispose();
            rangesObject?.Dispose();
            return;
        }
        var selected = CameraFrameRateSelector.SelectRange(
            candidates.Select(candidate => candidate.Common!.Value),
            _captureOptions.FrameRateMode,
            _captureOptions.TargetFrameRate);
        if (!selected.HasValue)
        {
            foreach (var candidate in candidates)
                candidate.Native.Dispose();
            rangesObject?.Dispose();
            return;
        }

        var match = candidates.First(candidate => candidate.Common == selected);
        _nativeFrameRateRange = match.Native;
        _effectiveNativeFrameRate = selected;
        foreach (var candidate in candidates)
        {
            if (!ReferenceEquals(candidate.Native, match.Native))
                candidate.Native.Dispose();
        }
        rangesObject?.Dispose();
    }

    private static IEnumerable<CameraFrameFormat> GetSupportedFrameFormats(
        StreamConfigurationMap configurationMap)
    {
        if (configurationMap.GetOutputSizes((int)ImageFormatType.Jpeg)?.Length > 0)
            yield return CameraFrameFormat.Jpeg;
        if (configurationMap.GetOutputSizes((int)ImageFormatType.Yuv420888)?.Length > 0)
            yield return CameraFrameFormat.Yuv420;
    }

    private static CameraControlCapabilities GetControlCapabilities(
        CameraCharacteristics characteristics)
    {
        var minimumZoomFactor = 1d;
        var maximumZoomFactor =
            (characteristics.Get(CameraCharacteristics.ScalerAvailableMaxDigitalZoom)
                as Java.Lang.Float)?.DoubleValue() ?? 1d;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var zoomRange = characteristics.Get(
                CameraCharacteristics.ControlZoomRatioRange) as Android.Util.Range;
            minimumZoomFactor =
                (zoomRange?.Lower as Java.Lang.Float)?.DoubleValue() ?? minimumZoomFactor;
            maximumZoomFactor =
                (zoomRange?.Upper as Java.Lang.Float)?.DoubleValue() ?? maximumZoomFactor;
            zoomRange?.Dispose();
        }

        var flashAvailable = characteristics.Get(
            CameraCharacteristics.FlashInfoAvailable) as Java.Lang.Boolean;
        var isTorchSupported = flashAvailable?.BooleanValue() == true;
        flashAvailable?.Dispose();

        var focusModesObject = characteristics.Get(
            CameraCharacteristics.ControlAfAvailableModes);
#pragma warning disable CA1422
        var nativeFocusModes = focusModesObject is null
            ? []
            : JNIEnv.GetArray<int>(focusModesObject.Handle);
#pragma warning restore CA1422
        focusModesObject?.Dispose();
        var focusModes = new List<CameraFocusMode>();
        if (nativeFocusModes.Contains((int)ControlAFMode.ContinuousPicture) ||
            nativeFocusModes.Contains((int)ControlAFMode.ContinuousVideo))
        {
            focusModes.Add(CameraFocusMode.Continuous);
        }
        if (nativeFocusModes.Contains((int)ControlAFMode.Auto))
            focusModes.Add(CameraFocusMode.Single);

        var maximumFocusRegions =
            (characteristics.Get(CameraCharacteristics.ControlMaxRegionsAf)
                as Integer)?.IntValue() ?? 0;
        var isFocusPointSupported = maximumFocusRegions > 0;

        var compensationRange = characteristics.Get(
            CameraCharacteristics.ControlAeCompensationRange) as Android.Util.Range;
        var minimumCompensationIndex =
            (compensationRange?.Lower as Integer)?.IntValue() ?? 0;
        var maximumCompensationIndex =
            (compensationRange?.Upper as Integer)?.IntValue() ?? 0;
        var compensationStepValue = characteristics.Get(
            CameraCharacteristics.ControlAeCompensationStep) as Android.Util.Rational;
        var compensationStep = compensationStepValue?.DoubleValue() ?? 0;
        compensationRange?.Dispose();
        compensationStepValue?.Dispose();

        return new CameraControlCapabilities(
            Math.Max(double.Epsilon, minimumZoomFactor),
            Math.Max(minimumZoomFactor, maximumZoomFactor),
            isTorchSupported,
            isFocusPointSupported,
            focusModes,
            minimumCompensationIndex * compensationStep,
            maximumCompensationIndex * compensationStep,
            compensationStep);
    }

    private static CameraFrameRateRange? ToFrameRateRange(Android.Util.Range range)
    {
        var minimum = (range?.Lower as Integer)?.IntValue();
        var maximum = (range?.Upper as Integer)?.IntValue();
        return minimum > 0 && maximum >= minimum
            ? new CameraFrameRateRange(minimum.Value, maximum.Value)
            : null;
    }

    private static Size SelectPreviewSize(
        IEnumerable<Size> sizes,
        CameraResolution previewTarget)
    {
        var available = RequireSizes(sizes);
        var selected = CameraResolutionSelector.SelectPreviewResolution(
            available.Select(ToResolution),
            previewTarget)
            ?? throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "The camera exposes no compatible preview size.",
                false,
                "NoPreviewSizes"));
        return available.First(size => ToResolution(size) == selected);
    }

    private static Size[] RequireSizes(IEnumerable<Size> sizes)
    {
        var available = sizes?.ToArray() ?? [];
        if (available.Length == 0)
        {
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "The camera exposes no compatible output sizes.",
                false,
                "NoOutputSizes"));
        }

        return available;
    }

    private static CameraResolution ToResolution(Size size) =>
        new(size.Width, size.Height);

    private void StartPreview()
    {
        if (!_isRunning ||
            _cameraDevice is null ||
            _previewSize is null ||
            _imageReader is null)
            return;

        _previewSession?.Close();
        _previewSession?.Dispose();
        _previewSession = null;
        var holderSurface = _surfaceView.Holder.Surface;
        if (holderSurface is null || !holderSurface.IsValid)
            return;

        _surfaceView.Holder.SetFixedSize(_previewSize.Width, _previewSize.Height);
        _mainHandler.Post(() =>
        {
            if (_isRunning)
                ConfigureTransform(Width, Height);
        });
        _previewSurface = holderSurface;

        _previewBuilder?.Dispose();
        _previewBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
        _previewBuilder.AddTarget(_previewSurface);
        _previewBuilder.AddTarget(_imageReader.Surface);

        // Camera2's Key API requires boxed Java integers on every supported API level.
#pragma warning disable CA1422
        _previewBuilder.Set(CaptureRequest.ControlMode, new Integer((int)ControlMode.Auto));
        _previewBuilder.Set(CaptureRequest.JpegOrientation, new Integer(GetJpegOrientation()));
        if (_jpegQuality.HasValue)
            _previewBuilder.Set(
                CaptureRequest.JpegQuality,
                new Java.Lang.Byte((sbyte)_jpegQuality.Value));
        if (_nativeFrameRateRange is not null)
            _previewBuilder.Set(
                CaptureRequest.ControlAeTargetFpsRange,
                _nativeFrameRateRange);
        ApplyControlsToBuilder(_previewBuilder);
#pragma warning restore CA1422

        _effectiveConfiguration = new CameraCaptureConfiguration(
            _captureOptions,
            _captureResolution,
            ToResolution(_previewSize),
            _frameFormat == CameraFrameFormat.Jpeg ? _jpegQuality : null,
            _minimumFrameInterval,
            _frameFormat,
            _captureOptions.FrameDeliveryMode,
            _effectiveNativeFrameRate,
            _capabilities);

        _outputConfigurations =
        [
            new OutputConfiguration(_previewSurface),
            new OutputConfiguration(_imageReader.Surface)
        ];
        _sessionConfiguration = new SessionConfiguration(
            (int)SessionType.Regular,
            _outputConfigurations,
            Context.MainExecutor,
            new CameraCaptureStateListener(this));
        _cameraDevice.CreateCaptureSession(_sessionConfiguration);
    }

    private void TryStartPreview()
    {
        try
        {
            StartPreview();
        }
        catch (CameraAccessException exception)
        {
            ReportFailure(MapCameraAccessException(exception));
        }
        catch (System.Exception exception)
        {
            ReportFailure(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "Android could not configure the preview and camera controls.",
                true,
                exception.GetType().Name,
                exception));
        }
    }

    private void ApplyControlsToBuilder(CaptureRequest.Builder builder)
    {
        var controls = _effectiveControls;
        if (controls is null || _cameraCharacteristics is null)
            return;

#pragma warning disable CA1422
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            builder.Set(
                CaptureRequest.ControlZoomRatio,
                new Java.Lang.Float((float)controls.ZoomFactor));
        }
        else
        {
            var cropRegion = GetZoomCropRegion(controls.ZoomFactor);
            if (cropRegion is not null)
                builder.Set(CaptureRequest.ScalerCropRegion, cropRegion);
        }

        builder.Set(
            CaptureRequest.FlashMode,
            new Integer((int)(controls.TorchEnabled ? FlashMode.Torch : FlashMode.Off)));

        var compensationStep = controls.Capabilities.ExposureCompensationStep;
        var compensationIndex = compensationStep > 0
            ? (int)Math.Round(
                controls.ExposureCompensation / compensationStep,
                MidpointRounding.AwayFromZero)
            : 0;
        builder.Set(
            CaptureRequest.ControlAeExposureCompensation,
            new Integer(compensationIndex));

        if (controls.FocusMode.HasValue)
        {
            var nativeFocusMode = controls.FocusMode == CameraFocusMode.Single
                ? ControlAFMode.Auto
                : ControlAFMode.ContinuousPicture;
            builder.Set(
                CaptureRequest.ControlAfMode,
                new Integer((int)nativeFocusMode));
        }

        if (controls.FocusPoint.HasValue)
        {
            var meteringRegion = CreateMeteringRegion(controls.FocusPoint.Value);
            if (meteringRegion is not null)
            {
                builder.Set(CaptureRequest.ControlAfRegions, new[] { meteringRegion });
                var maximumExposureRegions =
                    (_cameraCharacteristics.Get(CameraCharacteristics.ControlMaxRegionsAe)
                        as Integer)?.IntValue() ?? 0;
                if (maximumExposureRegions > 0)
                    builder.Set(CaptureRequest.ControlAeRegions, new[] { meteringRegion });
            }
        }
        else
        {
            builder.Set(CaptureRequest.ControlAfRegions, null);
            builder.Set(CaptureRequest.ControlAeRegions, null);
        }
#pragma warning restore CA1422
    }

    private void SubmitControlRequest(bool triggerFocus)
    {
        if (_previewSession is null || _previewBuilder is null)
            return;

#pragma warning disable CA1422
        if (triggerFocus && _effectiveControls?.FocusMode == CameraFocusMode.Single)
        {
            _previewBuilder.Set(
                CaptureRequest.ControlAfTrigger,
                new Integer((int)ControlAFTrigger.Start));
            using var focusRequest = _previewBuilder.Build();
            _previewSession.Capture(focusRequest, null, _backgroundHandler);
            _previewBuilder.Set(
                CaptureRequest.ControlAfTrigger,
                new Integer((int)ControlAFTrigger.Idle));
        }
        else if (triggerFocus)
        {
            _previewBuilder.Set(
                CaptureRequest.ControlAfTrigger,
                new Integer((int)ControlAFTrigger.Cancel));
            using var cancelRequest = _previewBuilder.Build();
            _previewSession.Capture(cancelRequest, null, _backgroundHandler);
            _previewBuilder.Set(
                CaptureRequest.ControlAfTrigger,
                new Integer((int)ControlAFTrigger.Idle));
        }
        else
        {
            _previewBuilder.Set(
                CaptureRequest.ControlAfTrigger,
                new Integer((int)ControlAFTrigger.Idle));
        }

        using var repeatingRequest = _previewBuilder.Build();
        _previewSession.SetRepeatingRequest(
            repeatingRequest,
            null,
            _backgroundHandler);
#pragma warning restore CA1422
    }

    private Rect GetZoomCropRegion(double zoomFactor)
    {
        var activeArray = _cameraCharacteristics?.Get(
            CameraCharacteristics.SensorInfoActiveArraySize) as Rect;
        if (activeArray is null)
            return null;

        var width = Math.Max(1, (int)Math.Round(activeArray.Width() / zoomFactor));
        var height = Math.Max(1, (int)Math.Round(activeArray.Height() / zoomFactor));
        var left = activeArray.Left + (activeArray.Width() - width) / 2;
        var top = activeArray.Top + (activeArray.Height() - height) / 2;
        return new Rect(left, top, left + width, top + height);
    }

    private MeteringRectangle CreateMeteringRegion(CameraPoint previewPoint)
    {
        var activeArray = _cameraCharacteristics?.Get(
            CameraCharacteristics.SensorInfoActiveArraySize) as Rect;
        if (activeArray is null)
            return null;

        var displayRotation = GetDisplayRotationDegrees(
            _surfaceView.Display?.Rotation ?? SurfaceOrientation.Rotation0);
        var sensorOrientation =
            (_cameraCharacteristics.Get(CameraCharacteristics.SensorOrientation)
                as Integer)?.IntValue() ?? 0;
        var relativeRotation = CameraPreviewTransformCalculator.ComputeRelativeRotation(
            sensorOrientation,
            displayRotation,
            _cameraOption == CameraOptions.Front);
        var sensorPoint = CameraControlPointMapper.ToSensorPoint(
            previewPoint,
            Width,
            Height,
            _previewSize?.Width ?? 1,
            _previewSize?.Height ?? 1,
            relativeRotation,
            _effectiveControls?.IsPreviewMirrored == true);
        var bounds = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? activeArray
            : GetZoomCropRegion(_effectiveControls?.ZoomFactor ?? 1) ?? activeArray;
        var regionWidth = Math.Max(1, bounds.Width() / 10);
        var regionHeight = Math.Max(1, bounds.Height() / 10);
        var centerX = bounds.Left + (int)Math.Round(sensorPoint.X * bounds.Width());
        var centerY = bounds.Top + (int)Math.Round(sensorPoint.Y * bounds.Height());
        var left = Math.Clamp(centerX - regionWidth / 2, bounds.Left, bounds.Right - regionWidth);
        var top = Math.Clamp(centerY - regionHeight / 2, bounds.Top, bounds.Bottom - regionHeight);
        return new MeteringRectangle(
            new Rect(left, top, left + regionWidth, top + regionHeight),
            MeteringRectangle.MeteringWeightMax);
    }

    private void OnCaptureSessionConfigured(CameraCaptureSession session)
    {
        DisposeSessionConfiguration();

        if (!_isRunning || _cameraDevice is null || _previewBuilder is null)
        {
            session.Close();
            session.Dispose();
            return;
        }

        _previewSession = session;
        try
        {
            SubmitControlRequest(true);
            _configurationSelected?.Invoke(_effectiveConfiguration);
            _controlsSelected?.Invoke(_effectiveControls);
            _captureStarted?.Invoke();
        }
        catch (CameraAccessException exception)
        {
            _previewSession.Close();
            _previewSession.Dispose();
            _previewSession = null;
            ReportFailure(MapCameraAccessException(exception));
        }
        catch (System.Exception exception)
        {
            _previewSession.Close();
            _previewSession.Dispose();
            _previewSession = null;
            ReportFailure(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "Android could not start the preview with the selected camera controls.",
                true,
                exception.GetType().Name,
                exception));
        }
    }

    private void OnCaptureSessionConfigureFailed(CameraCaptureSession session)
    {
        DisposeSessionConfiguration();
        session.Close();
        session.Dispose();
        ReportFailure(new CameraFailure(
            CameraErrorCode.SessionConfigurationFailed,
            "Android could not configure the camera capture session.",
            true,
            "CaptureSessionConfigurationFailed"));
    }

    private void OnCameraOpened(CameraDevice cameraDevice)
    {
        if (!_isRunning)
        {
            cameraDevice.Close();
            cameraDevice.Dispose();
            return;
        }

        _cameraDevice = cameraDevice;
        TryStartPreview();
    }

    private void OnCameraClosed(CameraDevice cameraDevice)
    {
        cameraDevice.Close();
        cameraDevice.Dispose();

        if (ReferenceEquals(_cameraDevice, cameraDevice))
            _cameraDevice = null;
    }

    private void OnCameraDisconnected(CameraDevice cameraDevice)
    {
        OnCameraClosed(cameraDevice);
        ReportFailure(new CameraFailure(
            CameraErrorCode.DeviceDisconnected,
            "The Android camera was disconnected.",
            true,
            "CameraDisconnected"));
    }

    private void OnCameraError(CameraDevice cameraDevice, CameraError error)
    {
        OnCameraClosed(cameraDevice);
        ReportFailure(MapCameraError(error));
    }

    private void OnImageAvailable(ImageReader reader)
    {
        global::Android.Media.Image image = null;
        try
        {
            image = _captureOptions.FrameDeliveryMode == CameraFrameDeliveryMode.Sequential
                ? reader.AcquireNextImage()
                : reader.AcquireLatestImage();
            if (image is null)
                return;

            if (_minimumFrameInterval > TimeSpan.Zero)
            {
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                var elapsed = (now - Interlocked.Read(ref _lastFrameTicks)) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (elapsed < _minimumFrameInterval.TotalSeconds) return;
                Interlocked.Exchange(ref _lastFrameTicks, now);
            }
            CameraFrameBuffer frameBuffer;
            if (_frameFormat == CameraFrameFormat.Jpeg)
            {
                var buffer = image.GetPlanes()?.FirstOrDefault()?.Buffer;
                if (buffer is null)
                    return;

                var bytes = new byte[buffer.Remaining()];
                buffer.Get(bytes);
                frameBuffer = new ManagedCameraFrameBuffer(bytes);
            }
            else
            {
                frameBuffer = new AndroidImageFrameBuffer(image);
                image = null;
            }

            var callback = _frameCaptured;
            if (callback is null)
            {
                frameBuffer.Release();
                return;
            }

            callback(
                frameBuffer,
                _frameFormat,
                frameBuffer is AndroidImageFrameBuffer nativeBuffer
                    ? nativeBuffer.Width
                    : image.Width,
                frameBuffer is AndroidImageFrameBuffer nativeHeightBuffer
                    ? nativeHeightBuffer.Height
                    : image.Height,
                DateTimeOffset.UtcNow,
                _effectiveConfiguration,
                _frameFormat == CameraFrameFormat.Jpeg ? 0 : GetJpegOrientation(),
                false);
        }
        catch (IllegalStateException)
        {
            // The reader can be closed while a final callback is still queued.
        }
        catch (System.Exception exception)
        {
            ReportFailure(new CameraFailure(
                CameraErrorCode.CaptureFailed,
                "Android could not deliver a camera frame.",
                true,
                exception.GetType().Name,
                exception));
        }
        finally
        {
            image?.Close();
            image?.Dispose();
        }
    }

    private sealed class AndroidImageFrameBuffer : CameraFrameBuffer
    {
        private global::Android.Media.Image _image;
        private readonly IntPtr[] _addresses;
        private readonly CameraFramePlaneDescription[] _descriptions;

        internal AndroidImageFrameBuffer(global::Android.Media.Image image)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));
            Width = image.Width;
            Height = image.Height;
            var planes = image.GetPlanes() ?? [];
            if (planes.Length == 0)
                throw new InvalidOperationException("The YUV image exposes no planes.");

            _addresses = new IntPtr[planes.Length];
            _descriptions = new CameraFramePlaneDescription[planes.Length];
            for (var index = 0; index < planes.Length; index++)
            {
                var plane = planes[index];
                var buffer = plane.Buffer ??
                    throw new InvalidOperationException("A YUV plane exposes no buffer.");
                var address = buffer.GetDirectBufferAddress();
                if (address == IntPtr.Zero)
                    throw new InvalidOperationException("A YUV plane is not backed by a direct buffer.");

                _addresses[index] = IntPtr.Add(address, buffer.Position());
                _descriptions[index] = new CameraFramePlaneDescription(
                    buffer.Remaining(),
                    plane.RowStride,
                    plane.PixelStride,
                    index == 0 ? Width : (Width + 1) / 2,
                    index == 0 ? Height : (Height + 1) / 2);
            }
        }

        internal int Width { get; }

        internal int Height { get; }

        internal override int PlaneCount => _descriptions.Length;

        internal override CameraFramePlaneDescription GetPlaneDescription(int index) =>
            _descriptions[index];

        internal override unsafe ReadOnlySpan<byte> GetPlaneSpan(int index)
        {
            var description = _descriptions[index];
            return new ReadOnlySpan<byte>((void*)_addresses[index], description.Length);
        }

        protected override void DisposeCore()
        {
            var image = Interlocked.Exchange(ref _image, null);
            image?.Close();
            image?.Dispose();
        }
    }

    private void ConfigureTransform(int viewWidth, int viewHeight)
    {
        if (_previewSize is null ||
            _cameraCharacteristics is null ||
            viewWidth == 0 ||
            viewHeight == 0)
            return;

        var rotation = _surfaceView.Display?.Rotation ?? SurfaceOrientation.Rotation0;
        var sensorValue = _cameraCharacteristics.Get(
            CameraCharacteristics.SensorOrientation) as Integer;
        var facingValue = _cameraCharacteristics.Get(
            CameraCharacteristics.LensFacing) as Integer;
        var transform = CameraPreviewTransformCalculator.Calculate(
            viewWidth,
            viewHeight,
            _previewSize.Width,
            _previewSize.Height,
            sensorValue?.IntValue() ?? 0,
            GetDisplayRotationDegrees(rotation),
            facingValue?.IntValue() == (int)LensFacing.Front,
            _effectiveControls?.IsPreviewMirrored == true);

        var previewLayout = new FrameLayout.LayoutParams(
            transform.Width,
            transform.Height)
        {
            Gravity = GravityFlags.Center
        };
        if (_surfaceView.Parent is null)
            _previewHost.AddView(_surfaceView, previewLayout);
        else if (_surfaceView.LayoutParameters is not FrameLayout.LayoutParams currentLayout ||
                 currentLayout.Width != transform.Width ||
                 currentLayout.Height != transform.Height)
            _surfaceView.LayoutParameters = previewLayout;
        _previewHost.ScaleX = transform.IsMirrored ? -1f : 1f;
    }

    private int GetJpegOrientation()
    {
        var rotation = _surfaceView.Display?.Rotation ?? SurfaceOrientation.Rotation0;
        var displayDegrees = GetDisplayRotationDegrees(rotation);
        var sensorValue = _cameraCharacteristics?.Get(CameraCharacteristics.SensorOrientation) as Integer;
        var sensorDegrees = sensorValue?.IntValue() ?? 0;

        return _cameraOption == CameraOptions.Front
            ? (sensorDegrees + displayDegrees) % 360
            : (sensorDegrees - displayDegrees + 360) % 360;
    }

    private static int GetDisplayRotationDegrees(SurfaceOrientation rotation) =>
        rotation switch
        {
            SurfaceOrientation.Rotation90 => 90,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 270,
            _ => 0
        };

    private void RegisterDisplayListener()
    {
        if (_displayManager is not null)
            return;

        _displayManager = Context.GetSystemService(Context.DisplayService) as DisplayManager;
        _displayManager?.RegisterDisplayListener(_displayRotationListener, _mainHandler);
    }

    private void UnregisterDisplayListener()
    {
        _displayManager?.UnregisterDisplayListener(_displayRotationListener);
        _displayManager = null;
    }

    private void OnDisplayChanged(int displayId)
    {
        var display = _surfaceView.Display;
        if (display is null || display.DisplayId != displayId)
            return;

        ConfigureTransform(Width, Height);
    }

    private void StartBackgroundThread()
    {
        _backgroundThread = new HandlerThread("Camera");
        _backgroundThread.Start();
        _backgroundHandler = new Handler(_backgroundThread.Looper);
    }

    private void StopBackgroundThread()
    {
        if (_backgroundThread is null)
            return;

        _backgroundThread.QuitSafely();
        try
        {
            _backgroundThread.Join(1000);
        }
        catch (InterruptedException)
        {
            System.Threading.Thread.CurrentThread.Interrupt();
        }

        _backgroundHandler?.Dispose();
        _backgroundHandler = null;
        _backgroundThread.Dispose();
        _backgroundThread = null;
    }

    private void DisposeSessionConfiguration()
    {
        _sessionConfiguration?.Dispose();
        _sessionConfiguration = null;

        if (_outputConfigurations is not null)
        {
            foreach (var outputConfiguration in _outputConfigurations)
                outputConfiguration.Dispose();
        }
        _outputConfigurations = null;

    }

    private void ReportFailure(CameraFailure failure) =>
        _captureFailed?.Invoke(failure);

    private static CameraFailure MapCameraAccessException(CameraAccessException exception) =>
        (int)exception.Reason switch
        {
            1 => new CameraFailure(
                CameraErrorCode.PermissionDenied,
                "Camera access is disabled by Android policy.",
                false,
                exception.Reason.ToString(),
                exception),
            2 => new CameraFailure(
                CameraErrorCode.DeviceDisconnected,
                "The Android camera is disconnected.",
                true,
                exception.Reason.ToString(),
                exception),
            4 or 5 => new CameraFailure(
                CameraErrorCode.CameraInUse,
                "The Android camera is already in use.",
                true,
                exception.Reason.ToString(),
                exception),
            _ => new CameraFailure(
                CameraErrorCode.CameraUnavailable,
                "Android could not access the camera.",
                true,
                exception.Reason.ToString(),
                exception)
        };

    private static CameraFailure MapCameraError(CameraError error) =>
        (int)error switch
        {
            1 or 2 => new CameraFailure(
                CameraErrorCode.CameraInUse,
                "The Android camera is already in use.",
                true,
                error.ToString()),
            3 => new CameraFailure(
                CameraErrorCode.PermissionDenied,
                "Camera access is disabled by Android policy.",
                false,
                error.ToString()),
            4 => new CameraFailure(
                CameraErrorCode.CameraUnavailable,
                "The Android camera device reported a fatal error.",
                true,
                error.ToString()),
            _ => new CameraFailure(
                CameraErrorCode.CameraUnavailable,
                "The Android camera service reported a fatal error.",
                true,
                error.ToString())
        };

    private sealed class CameraSurfaceHolderCallback(NativeCameraView owner)
        : Java.Lang.Object, ISurfaceHolderCallback
    {
        public void SurfaceCreated(ISurfaceHolder holder)
        {
            owner.ConfigureTransform(owner.Width, owner.Height);
            owner.TryStartPreview();
        }

        public void SurfaceChanged(
            ISurfaceHolder holder,
            Format format,
            int width,
            int height) => owner.ConfigureTransform(owner.Width, owner.Height);

        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
        }
    }

    private sealed class CameraStateListener(NativeCameraView owner) : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera) => owner.OnCameraOpened(camera);

        public override void OnDisconnected(CameraDevice camera) => owner.OnCameraDisconnected(camera);

        public override void OnError(CameraDevice camera, CameraError error) => owner.OnCameraError(camera, error);
    }

    private sealed class CameraCaptureStateListener(NativeCameraView owner)
        : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) =>
            owner.OnCaptureSessionConfigured(session);

        public override void OnConfigureFailed(CameraCaptureSession session)
            => owner.OnCaptureSessionConfigureFailed(session);
    }

    private sealed class ImageAvailableListener(NativeCameraView owner)
        : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader reader) => owner.OnImageAvailable(reader);
    }

    private sealed class DisplayRotationListener(NativeCameraView owner)
        : Java.Lang.Object, DisplayManager.IDisplayListener
    {
        public void OnDisplayAdded(int displayId)
        {
        }

        public void OnDisplayChanged(int displayId) => owner.OnDisplayChanged(displayId);

        public void OnDisplayRemoved(int displayId)
        {
        }
    }

}
