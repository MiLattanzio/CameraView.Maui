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
    private Action<byte[]> _frameCaptured;
    private Action _captureStarted;
    private Action _captureSuspended;
    private Action<CameraFailure> _captureFailed;
    private NSObject _runtimeErrorObserver;
    private NSObject _wasInterruptedObserver;
    private NSObject _interruptionEndedObserver;
    private CameraOptions _cameraOption;
    private CameraOrientation _orientation;
    private bool _isRunning;

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
            _captureSession = new AVCaptureSession
            {
                SessionPreset = AVCaptureSession.Preset1280x720
            };

            _captureSession.BeginConfiguration();
            try
            {
                ConfigureInput(cameraOption);
                ConfigureOutput();
            }
            finally
            {
                _captureSession.CommitConfiguration();
            }

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

    private void ConfigureInput(CameraOptions cameraOption)
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

        NSError configurationError = null;
        if (device.IsFocusModeSupported(AVCaptureFocusMode.ContinuousAutoFocus) &&
            device.LockForConfiguration(out configurationError))
        {
            device.FocusMode = AVCaptureFocusMode.ContinuousAutoFocus;
            device.UnlockForConfiguration();
        }
        configurationError?.Dispose();

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
    }

    private void ConfigureOutput()
    {
        _captureDelegate = new VideoCaptureDelegate(EmitFrame, ReportFailure);
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

    private void EmitFrame(byte[] bytes)
    {
        if (_isRunning)
            _frameCaptured?.Invoke(bytes);
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
        Action<byte[]> frameCaptured,
        Action<CameraFailure> captureFailed)
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
                using var imageBuffer = sampleBuffer.GetImageBuffer();
                if (imageBuffer is null)
                    return;

                using var image = new CIImage(imageBuffer);
                using var cgImage = _imageContext.CreateCGImage(image, image.Extent);
                if (cgImage is null)
                    return;

                using var uiImage = new UIImage(cgImage);
                using var imageData = uiImage.AsJPEG(0.85f);
                if (imageData is not null)
                    frameCaptured(imageData.ToArray());
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _imageContext.Dispose();

            base.Dispose(disposing);
        }
    }
}
