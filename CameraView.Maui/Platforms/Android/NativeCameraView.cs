using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Widget;
using Java.Lang;
using Size = Android.Util.Size;

namespace CameraView.Maui;

public sealed class NativeCameraView : FrameLayout
{
    private readonly AutoFitTextureView _textureView;
    private readonly CameraSurfaceTextureListener _surfaceTextureListener;
    private readonly CameraStateListener _cameraStateListener;
    private readonly ImageAvailableListener _imageAvailableListener;

    private CameraManager _cameraManager;
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
    private Action<byte[]> _frameCaptured;
    private Action _captureStarted;
    private Action _captureSuspended;
    private Action<CameraFailure> _captureFailed;
    private CameraOptions _cameraOption;
    private CameraOrientation _orientation;
    private bool _isRunning;

    public NativeCameraView(Context context) : base(context)
    {
        _textureView = new AutoFitTextureView(context);
        _surfaceTextureListener = new CameraSurfaceTextureListener(this);
        _cameraStateListener = new CameraStateListener(this);
        _imageAvailableListener = new ImageAvailableListener(this);
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
        Start(cameraOption, orientation, frameCaptured, null, null, null);

    internal void Start(
        CameraOptions cameraOption,
        CameraOrientation orientation,
        Action<byte[]> frameCaptured,
        Action captureStarted,
        Action captureSuspended,
        Action<CameraFailure> captureFailed)
    {
        if (_isRunning &&
            _cameraOption == cameraOption &&
            _orientation == orientation)
        {
            _frameCaptured = frameCaptured;
            _captureStarted = captureStarted;
            _captureSuspended = captureSuspended;
            _captureFailed = captureFailed;
            return;
        }

        Stop();

        _cameraOption = cameraOption;
        _orientation = orientation;
        _frameCaptured = frameCaptured;
        _captureStarted = captureStarted;
        _captureSuspended = captureSuspended;
        _captureFailed = captureFailed;

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

            _previewSize = SelectOutputSize(
                configurationMap.GetOutputSizes(Class.FromType(typeof(SurfaceTexture))));
            var captureSize = SelectOutputSize(
                configurationMap.GetOutputSizes((int)ImageFormatType.Jpeg));

            _imageReader = ImageReader.NewInstance(
                captureSize.Width,
                captureSize.Height,
                ImageFormatType.Jpeg,
                2);
            _imageReader.SetOnImageAvailableListener(_imageAvailableListener, _backgroundHandler);

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
            Stop();

        base.Dispose(disposing);
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

    private static Size SelectOutputSize(IEnumerable<Size> sizes)
    {
        if (sizes is null)
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "The camera exposes no compatible output sizes.",
                false,
                "NoOutputSizes"));

        var availableSizes = sizes.ToArray();
        if (availableSizes.Length == 0)
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "The camera exposes no compatible output sizes.",
                false,
                "NoOutputSizes"));

        return availableSizes
                   .Where(size => size.Width <= 1280 && size.Height <= 1280)
                   .OrderByDescending(GetArea)
                   .FirstOrDefault()
               ?? availableSizes.OrderBy(GetArea).First();
    }

    private static long GetArea(Size size) => (long)size.Width * size.Height;

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
#pragma warning restore CA1422

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
            image = reader.AcquireLatestImage();
            var buffer = image?.GetPlanes()?.FirstOrDefault()?.Buffer;
            if (buffer is null)
                return;

            var bytes = new byte[buffer.Remaining()];
            buffer.Get(bytes);
            _frameCaptured?.Invoke(bytes);
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

    private void ConfigureTransform(int viewWidth, int viewHeight)
    {
        if (_previewSize is null || viewWidth == 0 || viewHeight == 0)
            return;

        var activity = FindActivity(Context);
        var rotation = activity?.WindowManager?.DefaultDisplay?.Rotation
                       ?? SurfaceOrientation.Rotation0;
        var matrix = new Matrix();
        var viewRect = new global::Android.Graphics.RectF(0, 0, viewWidth, viewHeight);
        var bufferRect = new global::Android.Graphics.RectF(0, 0, _previewSize.Height, _previewSize.Width);
        var centerX = viewRect.CenterX();
        var centerY = viewRect.CenterY();

        if (rotation is SurfaceOrientation.Rotation90 or SurfaceOrientation.Rotation270)
        {
            bufferRect.Offset(centerX - bufferRect.CenterX(), centerY - bufferRect.CenterY());
            matrix.SetRectToRect(viewRect, bufferRect, Matrix.ScaleToFit.Fill);
            var scale = System.Math.Max(
                (float)viewHeight / _previewSize.Height,
                (float)viewWidth / _previewSize.Width);
            matrix.PostScale(scale, scale, centerX, centerY);
            matrix.PostRotate(90 * ((int)rotation - 2), centerX, centerY);
        }
        else if (rotation == SurfaceOrientation.Rotation180)
        {
            matrix.PostRotate(180, centerX, centerY);
        }

        _textureView.SetTransform(matrix);

        if (viewHeight >= viewWidth)
            _textureView.SetAspectRatio(_previewSize.Height, _previewSize.Width);
        else
            _textureView.SetAspectRatio(_previewSize.Width, _previewSize.Height);
    }

    private int GetJpegOrientation()
    {
        var activity = FindActivity(Context);
        var rotation = activity?.WindowManager?.DefaultDisplay?.Rotation
                       ?? SurfaceOrientation.Rotation0;
        var displayDegrees = rotation switch
        {
            SurfaceOrientation.Rotation90 => 90,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 270,
            _ => 0
        };
        var sensorValue = _cameraCharacteristics?.Get(CameraCharacteristics.SensorOrientation) as Integer;
        var sensorDegrees = sensorValue?.IntValue() ?? 0;

        return _cameraOption == CameraOptions.Front
            ? (sensorDegrees + displayDegrees) % 360
            : (sensorDegrees - displayDegrees + 360) % 360;
    }

    private static Activity FindActivity(Context context)
    {
        while (context is ContextWrapper wrapper)
        {
            if (context is Activity activity)
                return activity;

            context = wrapper.BaseContext;
        }

        return context as Activity;
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

}

internal sealed class AutoFitTextureView : TextureView
{
    private int _ratioWidth;
    private int _ratioHeight;

    public AutoFitTextureView(Context context) : base(context)
    {
    }

    public void SetAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        _ratioWidth = width;
        _ratioHeight = height;
        RequestLayout();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var height = MeasureSpec.GetSize(heightMeasureSpec);

        if (_ratioWidth == 0 || _ratioHeight == 0)
            SetMeasuredDimension(width, height);
        // AspectFill: keep the camera ratio while allowing one dimension to
        // exceed the parent. NativeCameraView centers and clips the overflow.
        else if (width < (float)height * _ratioWidth / _ratioHeight)
            SetMeasuredDimension(height * _ratioWidth / _ratioHeight, height);
        else
            SetMeasuredDimension(width, width * _ratioHeight / _ratioWidth);
    }
}
