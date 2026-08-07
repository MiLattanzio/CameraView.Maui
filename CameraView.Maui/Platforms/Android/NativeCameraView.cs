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
using Size = Android.Util.Size;

namespace CameraView.Maui;

public sealed class NativeCameraView : FrameLayout
{
    private readonly TextureView _textureView;
    private readonly CameraSurfaceTextureListener _surfaceTextureListener;
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
    private Action _captureStarted;
    private Action _captureSuspended;
    private Action<CameraFailure> _captureFailed;
    private CameraOptions _cameraOption;
    private CameraOrientation _orientation;
    private CameraCaptureOptions _captureOptions;
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
    private CameraFrameRateRange[] _availableFrameRateRanges = [];

    public NativeCameraView(Context context) : base(context)
    {
        _textureView = new TextureView(context);
        _surfaceTextureListener = new CameraSurfaceTextureListener(this);
        _cameraStateListener = new CameraStateListener(this);
        _imageAvailableListener = new ImageAvailableListener(this);
        _displayRotationListener = new DisplayRotationListener(this);
        _mainHandler = new Handler(Looper.MainLooper);
        _textureView.SurfaceTextureListener = _surfaceTextureListener;

        var previewLayout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent)
        {
            Gravity = GravityFlags.Center
        };
        AddView(_textureView, previewLayout);
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
            null,
            null,
            null,
            null);

    internal void Start(
        CameraOptions cameraOption,
        CameraOrientation orientation,
        Action<CameraFrameBuffer, CameraFrameFormat, int, int, DateTimeOffset, CameraCaptureConfiguration, int, bool> frameCaptured,
        CameraCaptureOptions captureOptions,
        Action<CameraCaptureConfiguration> configurationSelected,
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
            return;
        }

        Stop();

        _cameraOption = cameraOption;
        _orientation = orientation;
        _captureOptions = captureOptions;
        _frameCaptured = frameCaptured;
        _captureStarted = captureStarted;
        _captureSuspended = captureSuspended;
        _captureFailed = captureFailed;
        _configurationSelected = configurationSelected;
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
                _captureResolution);
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
            if (_textureView.IsAvailable)
                ConfigureTransform(_textureView.Width, _textureView.Height);

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

    public void Stop()
    {
        _isRunning = false;
        _frameCaptured = null;
        _captureStarted = null;
        _captureSuspended = null;
        _captureFailed = null;
        _configurationSelected = null;
        _captureOptions = null;
        _effectiveConfiguration = null;
        _captureResolution = CameraResolution.Default;
        _frameFormat = CameraFrameFormat.Jpeg;
        _nativeFrameRateRange?.Dispose();
        _nativeFrameRateRange = null;
        _effectiveNativeFrameRate = null;
        _capabilities = null;
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

        _previewSurface?.Dispose();
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
        ConfigureTransform(_textureView.Width, _textureView.Height);
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
        CameraResolution captureResolution)
    {
        var available = RequireSizes(sizes);
        var selected = CameraResolutionSelector.SelectPreviewResolution(
            available.Select(ToResolution),
            captureResolution)
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
            !_textureView.IsAvailable ||
            _previewSize is null ||
            _imageReader is null)
            return;

        _previewSession?.Close();
        _previewSession?.Dispose();
        _previewSession = null;
        _previewSurface?.Dispose();

        var texture = _textureView.SurfaceTexture;
        if (texture is null)
            return;

        texture.SetDefaultBufferSize(_previewSize.Width, _previewSize.Height);
        _previewSurface = new Surface(texture);

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
            _previewSession.SetRepeatingRequest(
                _previewBuilder.Build(),
                null,
                _backgroundHandler);
            _configurationSelected?.Invoke(_effectiveConfiguration);
            _captureStarted?.Invoke();
        }
        catch (CameraAccessException exception)
        {
            _previewSession.Close();
            _previewSession.Dispose();
            _previewSession = null;
            ReportFailure(MapCameraAccessException(exception));
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
        StartPreview();
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

        var rotation = _textureView.Display?.Rotation ?? SurfaceOrientation.Rotation0;
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
            facingValue?.IntValue() == (int)LensFacing.Front);

        var centerX = viewWidth / 2f;
        var centerY = viewHeight / 2f;
        var matrix = new Matrix();
        matrix.SetScale(transform.ScaleX, transform.ScaleY, centerX, centerY);
        matrix.PostRotate(transform.RotationDegrees, centerX, centerY);
        _textureView.SetTransform(matrix);
    }

    private int GetJpegOrientation()
    {
        var rotation = _textureView.Display?.Rotation ?? SurfaceOrientation.Rotation0;
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
        var display = _textureView.Display;
        if (display is null || display.DisplayId != displayId)
            return;

        ConfigureTransform(_textureView.Width, _textureView.Height);
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

    private sealed class CameraSurfaceTextureListener(NativeCameraView owner)
        : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
        {
            owner.ConfigureTransform(width, height);
            owner.StartPreview();
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) => true;

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) =>
            owner.ConfigureTransform(width, height);

        public void OnSurfaceTextureUpdated(SurfaceTexture surface)
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
