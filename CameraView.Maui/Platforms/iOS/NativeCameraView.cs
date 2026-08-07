using AVFoundation;
using CoreFoundation;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;
using UIKit;

namespace CameraView.Maui;

public sealed class NativeCameraView : UIView
{
    private readonly DispatchQueue _captureQueue = new("Camera");

    private AVCaptureSession _captureSession;
    private AVCaptureVideoPreviewLayer _previewLayer;
    private AVCaptureVideoDataOutput _videoOutput;
    private VideoCaptureDelegate _captureDelegate;
    private Action<byte[], int, int, DateTimeOffset, CameraCaptureConfiguration> _frameCaptured;
    private Action<CameraCaptureConfiguration> _configurationSelected;
    private Action _captureStarted;
    private Action _captureSuspended;
    private Action<CameraFailure> _captureFailed;
    private NSObject _runtimeErrorObserver;
    private NSObject _wasInterruptedObserver;
    private NSObject _interruptionEndedObserver;
    private CameraOptions _cameraOption;
    private CameraOrientation _orientation;
    private CameraCaptureOptions _captureOptions;
    private CameraCaptureConfiguration _effectiveConfiguration;
    private bool _isRunning;
    private int _jpegQuality;
    private TimeSpan _minimumFrameInterval;

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        if (_previewLayer is not null)
            _previewLayer.Frame = Bounds;
    }

    public void Start(
        CameraOptions cameraOption,
        CameraOrientation orientation,
        Action<byte[]> frameCaptured) =>
        Start(
            cameraOption,
            orientation,
            (bytes, _, _, _, _) => frameCaptured(bytes),
            CameraCaptureOptions.Default,
            null,
            null,
            null,
            null);

    internal void Start(
        CameraOptions cameraOption,
        CameraOrientation orientation,
        Action<byte[], int, int, DateTimeOffset, CameraCaptureConfiguration> frameCaptured,
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
        _jpegQuality = captureOptions.JpegQuality ?? 85;
        _minimumFrameInterval = captureOptions.GetEffectiveMinimumFrameInterval();

        try
        {
            _captureSession = new AVCaptureSession
            {
                SessionPreset = AVCaptureSession.PresetInputPriority
            };

            _captureSession.BeginConfiguration();
            CameraResolution captureResolution;
            try
            {
                captureResolution = ConfigureInput(
                    cameraOption,
                    captureOptions);
                ConfigureOutput();
            }
            finally
            {
                _captureSession.CommitConfiguration();
            }

            _effectiveConfiguration = new CameraCaptureConfiguration(
                captureOptions,
                captureResolution,
                captureResolution,
                _jpegQuality,
                _minimumFrameInterval);

            _previewLayer = new AVCaptureVideoPreviewLayer(_captureSession)
            {
                Frame = Bounds,
                VideoGravity = AVLayerVideoGravity.ResizeAspectFill
            };
            ConfigureConnection(_previewLayer.Connection);
            Layer.InsertSublayer(_previewLayer, 0);

            ObserveSession();
            _isRunning = true;
            _captureSession.StartRunning();
            if (!_captureSession.Running)
            {
                throw new CameraPlatformException(new CameraFailure(
                    CameraErrorCode.CameraUnavailable,
                    "iOS did not start the camera session.",
                    true,
                    "SessionNotRunning"));
            }

            _configurationSelected?.Invoke(_effectiveConfiguration);
            _captureStarted?.Invoke();
        }
        catch (CameraPlatformException)
        {
            Stop();
            throw;
        }
        catch (System.Exception exception)
        {
            Stop();
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.Unknown,
                "iOS could not start the camera.",
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

        DisposeSessionObservers();

        if (_captureSession?.Running == true)
            _captureSession.StopRunning();

        _videoOutput?.SetSampleBufferDelegate(null, null);
        _previewLayer?.RemoveFromSuperLayer();

        _captureDelegate?.Dispose();
        _captureDelegate = null;
        _videoOutput?.Dispose();
        _videoOutput = null;
        _previewLayer?.Dispose();
        _previewLayer = null;
        _captureSession?.Dispose();
        _captureSession = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _captureQueue.Dispose();
        }

        base.Dispose(disposing);
    }

    private CameraResolution ConfigureInput(
            CameraOptions cameraOption,
            CameraCaptureOptions captureOptions)
    {
        var position = cameraOption == CameraOptions.Front
            ? AVCaptureDevicePosition.Front
            : AVCaptureDevicePosition.Back;
        var device = AVCaptureDevice.GetDefaultDevice(
            AVCaptureDeviceType.BuiltInWideAngleCamera,
            AVMediaTypes.Video,
            position)
            ?? throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.CameraUnavailable,
                $"No {cameraOption} camera was found.",
                false,
                "CameraNotFound"));

        var deviceConfiguration = ConfigureDevice(device, captureOptions);

        var input = AVCaptureDeviceInput.FromDevice(device, out var inputError);
        if (input is null)
        {
            var message = inputError?.LocalizedDescription ?? "Unknown camera input error.";
            var failure = inputError is null
                ? new CameraFailure(
                    CameraErrorCode.CameraUnavailable,
                    message,
                    true,
                    "CameraInputUnavailable")
                : MapAvError(inputError, message);
            inputError?.Dispose();
            throw new CameraPlatformException(failure);
        }
        inputError?.Dispose();

        if (!_captureSession.CanAddInput(input))
        {
            input.Dispose();
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "The camera input cannot be added to the capture session.",
                true,
                "CannotAddCameraInput"));
        }

        _captureSession.AddInput(input);
        return deviceConfiguration;
    }

    private CameraResolution ConfigureDevice(
            AVCaptureDevice device,
            CameraCaptureOptions captureOptions)
    {
        var formats = device.Formats
            .Select(format => new
            {
                Format = format,
                Description = format.FormatDescription as CMVideoFormatDescription
            })
            .Where(candidate => candidate.Description is not null)
            .Select(candidate => new
            {
                candidate.Format,
                Resolution = new CameraResolution(
                    candidate.Description.Dimensions.Width,
                    candidate.Description.Dimensions.Height)
            })
            .ToArray();

        var selectedResolution = CameraResolutionSelector.SelectCaptureResolution(
            formats.Select(candidate => candidate.Resolution),
            captureOptions);
        if (selectedResolution is null)
        {
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                $"The requested exact resolution {captureOptions.PreferredResolution} is unavailable.",
                false,
                "ExactResolutionUnavailable"));
        }

        var selectedFormat = formats
            .Where(candidate => candidate.Resolution == selectedResolution.Value)
            .Select(candidate => candidate.Format)
            .First();

        if (!device.LockForConfiguration(out var configurationError))
        {
            var message = configurationError?.LocalizedDescription ??
                          "The camera device could not be configured.";
            configurationError?.Dispose();
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                message,
                true,
                "DeviceConfigurationLockFailed"));
        }

        try
        {
            device.ActiveFormat = selectedFormat;

            if (device.IsFocusModeSupported(AVCaptureFocusMode.ContinuousAutoFocus))
                device.FocusMode = AVCaptureFocusMode.ContinuousAutoFocus;
        }
        finally
        {
            device.UnlockForConfiguration();
            configurationError?.Dispose();
        }

        return selectedResolution.Value;
    }

    private void ConfigureOutput()
    {
        _captureDelegate = new VideoCaptureDelegate(
            (bytes, width, height, timestamp) =>
            {
                if (_isRunning)
                {
                    _frameCaptured?.Invoke(
                        bytes,
                        width,
                        height,
                        timestamp,
                        _effectiveConfiguration);
                }
            },
            ReportFailure,
            () => _minimumFrameInterval,
            () => _jpegQuality);
        _videoOutput = new AVCaptureVideoDataOutput
        {
            AlwaysDiscardsLateVideoFrames = true
        };

        var settings = new CVPixelBufferAttributes
        {
            PixelFormatType = CVPixelFormatType.CV32BGRA
        };
        _videoOutput.WeakVideoSettings = settings.Dictionary;

        _videoOutput.SetSampleBufferDelegate(_captureDelegate, _captureQueue);
        if (!_captureSession.CanAddOutput(_videoOutput))
        {
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.SessionConfigurationFailed,
                "The camera output cannot be added to the capture session.",
                true,
                "CannotAddVideoOutput"));
        }

        _captureSession.AddOutput(_videoOutput);
        ConfigureConnection(_videoOutput.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()));
    }

    private void ConfigureConnection(AVCaptureConnection connection)
    {
        if (connection is null)
            return;

        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            var rotationAngle = _orientation == CameraOrientation.Landscape ? 0f : 90f;
            if (connection.IsVideoRotationAngleSupported(rotationAngle))
                connection.VideoRotationAngle = rotationAngle;
        }
        else if (connection.SupportsVideoOrientation)
        {
            connection.VideoOrientation = _orientation == CameraOrientation.Landscape
                ? AVCaptureVideoOrientation.LandscapeLeft
                : AVCaptureVideoOrientation.Portrait;
        }

        if (connection.SupportsVideoMirroring)
        {
            connection.AutomaticallyAdjustsVideoMirroring = false;
            connection.VideoMirrored = _cameraOption == CameraOptions.Front;
        }
    }

    private void ObserveSession()
    {
        _runtimeErrorObserver = AVCaptureSession.Notifications.ObserveRuntimeError(
            _captureSession,
            OnRuntimeError);
        _wasInterruptedObserver = AVCaptureSession.Notifications.ObserveWasInterrupted(
            _captureSession,
            OnWasInterrupted);
        _interruptionEndedObserver = AVCaptureSession.Notifications.ObserveInterruptionEnded(
            _captureSession,
            OnInterruptionEnded);
    }

    private void DisposeSessionObservers()
    {
        _runtimeErrorObserver?.Dispose();
        _runtimeErrorObserver = null;
        _wasInterruptedObserver?.Dispose();
        _wasInterruptedObserver = null;
        _interruptionEndedObserver?.Dispose();
        _interruptionEndedObserver = null;
    }

    private void OnRuntimeError(
        object sender,
        AVCaptureSessionRuntimeErrorEventArgs eventArgs) =>
        ReportFailure(MapAvError(eventArgs.Error, eventArgs.Error.LocalizedDescription));

    private void OnWasInterrupted(object sender, NSNotificationEventArgs eventArgs)
    {
        if (_isRunning)
            _captureSuspended?.Invoke();
    }

    private void OnInterruptionEnded(object sender, NSNotificationEventArgs eventArgs)
    {
        if (!_isRunning || _captureSession is null)
            return;

        try
        {
            if (!_captureSession.Running)
                _captureSession.StartRunning();

            if (_captureSession.Running)
                _captureStarted?.Invoke();
            else
                ReportFailure(new CameraFailure(
                    CameraErrorCode.CameraUnavailable,
                    "The iOS camera session did not resume after interruption.",
                    true,
                    "InterruptionResumeFailed"));
        }
        catch (System.Exception exception)
        {
            ReportFailure(new CameraFailure(
                CameraErrorCode.CameraUnavailable,
                "The iOS camera session could not resume after interruption.",
                true,
                exception.GetType().Name,
                exception));
        }
    }

    private void ReportFailure(CameraFailure failure) =>
        _captureFailed?.Invoke(failure);

    private static CameraFailure MapAvError(NSError error, string message)
    {
        var code = (AVError)(long)error.Code;
        var platformCode = $"{error.Domain}:{error.Code}";
        return code switch
        {
            AVError.ApplicationIsNotAuthorizedToUseDevice or
            AVError.ApplicationIsNotAuthorized => new CameraFailure(
                CameraErrorCode.PermissionDenied,
                message,
                false,
                platformCode),
            AVError.DeviceInUseByAnotherApplication or
            AVError.DeviceAlreadyUsedByAnotherSession or
            AVError.DeviceLockedForConfigurationByAnotherProcess => new CameraFailure(
                CameraErrorCode.CameraInUse,
                message,
                true,
                platformCode),
            AVError.DeviceNotConnected or
            AVError.DeviceWasDisconnected => new CameraFailure(
                CameraErrorCode.DeviceDisconnected,
                message,
                true,
                platformCode),
            AVError.NoDataCaptured => new CameraFailure(
                CameraErrorCode.CaptureFailed,
                message,
                true,
                platformCode),
            _ => new CameraFailure(
                CameraErrorCode.CameraUnavailable,
                message,
                true,
                platformCode)
        };
    }

    private sealed class VideoCaptureDelegate(
        Action<byte[], int, int, DateTimeOffset> frameCaptured,
        Action<CameraFailure> captureFailed,
        Func<TimeSpan> minimumFrameInterval,
        Func<int> jpegQuality)
        : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        private readonly CIContext _imageContext = CIContext.FromOptions(null);

        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput,
            CMSampleBuffer sampleBuffer,
            AVCaptureConnection connection)
        {
            try
            {
                var interval = minimumFrameInterval();
                if (interval > TimeSpan.Zero)
                {
                    var now = System.Diagnostics.Stopwatch.GetTimestamp();
                    var elapsed = (now - Interlocked.Read(ref _lastFrameTicks)) / (double)System.Diagnostics.Stopwatch.Frequency;
                    if (elapsed < interval.TotalSeconds) return;
                    Interlocked.Exchange(ref _lastFrameTicks, now);
                }
                using var imageBuffer = sampleBuffer.GetImageBuffer();
                if (imageBuffer is null)
                    return;

                var pixelBuffer = imageBuffer as CVPixelBuffer;
                var width = pixelBuffer?.Width ?? 0;
                var height = pixelBuffer?.Height ?? 0;
                using var image = new CIImage(imageBuffer);
                using var cgImage = _imageContext.CreateCGImage(image, image.Extent);
                if (cgImage is null)
                    return;

                using var uiImage = new UIImage(cgImage);
                using var imageData = uiImage.AsJPEG(Math.Clamp(jpegQuality() / 100f, 0.01f, 1f));
                if (imageData is not null)
                {
                    frameCaptured(
                        imageData.ToArray(),
                        (int)width,
                        (int)height,
                        DateTimeOffset.UtcNow);
                }
            }
            catch (System.Exception exception)
            {
                captureFailed(new CameraFailure(
                    CameraErrorCode.CaptureFailed,
                    "iOS could not encode a camera frame.",
                    true,
                    exception.GetType().Name,
                    exception));
            }
            finally
            {
                sampleBuffer.Dispose();
            }
        }

        private long _lastFrameTicks;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _imageContext.Dispose();

            base.Dispose(disposing);
        }
    }
}
