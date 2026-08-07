using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;
using ObjCRuntime;
using System.Runtime.InteropServices;
using UIKit;

namespace CameraView.Maui;

public sealed class NativeCameraView : UIView
{
    private readonly DispatchQueue _captureQueue = new("Camera");

    private AVCaptureSession _captureSession;
    private AVCaptureVideoPreviewLayer _previewLayer;
    private AVCaptureVideoDataOutput _videoOutput;
    private AVCaptureDevice _captureDevice;
    private VideoCaptureDelegate _captureDelegate;
    private Action<CameraFrameBuffer, CameraFrameFormat, int, int, DateTimeOffset, CameraCaptureConfiguration, int, bool> _frameCaptured;
    private Action<CameraCaptureConfiguration> _configurationSelected;
    private Action<CameraControlState> _controlsSelected;
    private Action _captureStarted;
    private Action _captureSuspended;
    private Action<CameraFailure> _captureFailed;
    private NSObject _runtimeErrorObserver;
    private NSObject _wasInterruptedObserver;
    private NSObject _interruptionEndedObserver;
    private CameraOptions _cameraOption;
    private CameraOrientation _orientation;
    private CameraCaptureOptions _captureOptions;
    private CameraControlOptions _controlOptions;
    private CameraCaptureConfiguration _effectiveConfiguration;
    private bool _isRunning;
    private int _jpegQuality;
    private TimeSpan _minimumFrameInterval;
    private CameraFrameFormat _frameFormat;
    private CameraFrameRateRange? _effectiveNativeFrameRate;
    private CameraCaptureCapabilities _capabilities;
    private CameraControlCapabilities _controlCapabilities;
    private CameraControlState _effectiveControls;
    private CameraResolution[] _availableCaptureResolutions = [];
    private CameraFrameRateRange[] _availableFrameRateRanges = [];
    private RawFrameCapacity _rawFrameCapacity;

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
        _jpegQuality = captureOptions.JpegQuality ?? 85;
        _minimumFrameInterval = captureOptions.GetEffectiveMinimumFrameInterval();
        _rawFrameCapacity = new RawFrameCapacity(captureOptions.MaxOutstandingFrames);

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
                _frameFormat == CameraFrameFormat.Jpeg ? _jpegQuality : null,
                _minimumFrameInterval,
                _frameFormat,
                captureOptions.FrameDeliveryMode,
                _effectiveNativeFrameRate,
                _capabilities);

            _previewLayer = new AVCaptureVideoPreviewLayer(_captureSession)
            {
                Frame = Bounds,
                VideoGravity = AVLayerVideoGravity.ResizeAspectFill
            };
            ConfigureConnection(_previewLayer.Connection, true);
            Layer.InsertSublayer(_previewLayer, 0);
            ApplyInitialControlsWithPreviewMapping();

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
            _controlsSelected?.Invoke(_effectiveControls);
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

    internal void UpdateControls(
        CameraControlOptions controlOptions,
        Action<CameraControlState> controlsSelected,
        Action<CameraFailure> controlsFailed)
    {
        ArgumentNullException.ThrowIfNull(controlOptions);
        controlOptions.Validate();
        _controlOptions = controlOptions;
        _controlsSelected = controlsSelected;

        if (!_isRunning || _captureDevice is null || _controlCapabilities is null)
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
            if (!_captureDevice.LockForConfiguration(out var configurationError))
            {
                var message = configurationError?.LocalizedDescription ??
                              "The camera controls could not be configured.";
                configurationError?.Dispose();
                throw new InvalidOperationException(message);
            }

            try
            {
                ApplyControlsToDevice(_captureDevice, focusChanged);
            }
            finally
            {
                _captureDevice.UnlockForConfiguration();
                configurationError?.Dispose();
            }

            ConfigureConnection(_previewLayer?.Connection, true);
            _controlsSelected?.Invoke(_effectiveControls);
        }
        catch (System.Exception exception)
        {
            _effectiveControls = previousControls;
            ConfigureConnection(_previewLayer?.Connection, true);
            controlsFailed?.Invoke(new CameraFailure(
                CameraErrorCode.ControlConfigurationFailed,
                "iOS could not apply the requested camera controls.",
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
        _frameFormat = CameraFrameFormat.Jpeg;
        _effectiveNativeFrameRate = null;
        _capabilities = null;
        _controlCapabilities = null;
        _effectiveControls = null;
        _availableCaptureResolutions = [];
        _availableFrameRateRanges = [];
        _rawFrameCapacity = null;

        DisposeSessionObservers();

        if (_captureSession?.Running == true)
            _captureSession.StopRunning();

        _videoOutput?.SetSampleBufferDelegate(null, null);
        _previewLayer?.RemoveFromSuperLayer();

        _captureDelegate?.Dispose();
        _captureDelegate = null;
        _videoOutput?.Dispose();
        _videoOutput = null;
        _captureDevice?.Dispose();
        _captureDevice = null;
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

        _captureDevice = device;
        _controlCapabilities = GetControlCapabilities(device);
        _effectiveControls = CameraControlNegotiator.Negotiate(
            _controlOptions,
            _controlCapabilities,
            _cameraOption);

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
                Description = format.FormatDescription as CMVideoFormatDescription,
                FrameRateRanges = format.VideoSupportedFrameRateRanges
                    .Select(range => new CameraFrameRateRange(
                        range.MinFrameRate,
                        range.MaxFrameRate))
                    .ToArray()
            })
            .Where(candidate => candidate.Description is not null)
            .Select(candidate => new
            {
                candidate.Format,
                candidate.FrameRateRanges,
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

        _availableCaptureResolutions = formats
            .Select(candidate => candidate.Resolution)
            .Distinct()
            .ToArray();
        _availableFrameRateRanges = formats
            .SelectMany(candidate => candidate.FrameRateRanges)
            .Distinct()
            .ToArray();

        var resolutionFormats = formats
            .Where(candidate => candidate.Resolution == selectedResolution.Value)
            .ToArray();
        var selectedRange = CameraFrameRateSelector.SelectRange(
            resolutionFormats.SelectMany(candidate => candidate.FrameRateRanges),
            captureOptions.FrameRateMode,
            captureOptions.TargetFrameRate);
        var selectedFormat = selectedRange.HasValue
            ? resolutionFormats.First(candidate =>
                candidate.FrameRateRanges.Contains(selectedRange.Value)).Format
            : resolutionFormats.First().Format;

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

            if (selectedRange.HasValue)
            {
                var selectedFrameRate = CameraFrameRateSelector.SelectFrameRate(
                    selectedRange.Value,
                    captureOptions.FrameRateMode,
                    captureOptions.TargetFrameRate);
                var duration = CMTime.FromSeconds(1d / selectedFrameRate, 1_000_000);
                device.ActiveVideoMinFrameDuration = duration;
                device.ActiveVideoMaxFrameDuration = duration;
                _effectiveNativeFrameRate = new CameraFrameRateRange(
                    selectedFrameRate,
                    selectedFrameRate);
            }
            else
            {
                _effectiveNativeFrameRate = null;
            }

            ApplyControlsToDevice(device, false);
        }
        finally
        {
            device.UnlockForConfiguration();
            configurationError?.Dispose();
        }

        return selectedResolution.Value;
    }

    private static CameraControlCapabilities GetControlCapabilities(
        AVCaptureDevice device)
    {
        var focusModes = new List<CameraFocusMode>();
        if (device.IsFocusModeSupported(AVCaptureFocusMode.ContinuousAutoFocus))
            focusModes.Add(CameraFocusMode.Continuous);
        if (device.IsFocusModeSupported(AVCaptureFocusMode.AutoFocus))
            focusModes.Add(CameraFocusMode.Single);

        return new CameraControlCapabilities(
            (double)device.MinAvailableVideoZoomFactor,
            (double)device.MaxAvailableVideoZoomFactor,
            device.HasTorch && device.IsTorchModeSupported(AVCaptureTorchMode.On),
            device.FocusPointOfInterestSupported,
            focusModes,
            device.MinExposureTargetBias,
            device.MaxExposureTargetBias,
            0);
    }

    private void ApplyControlsToDevice(AVCaptureDevice device, bool applyFocus)
    {
        var controls = _effectiveControls;
        if (controls is null)
            return;

        device.VideoZoomFactor = (nfloat)controls.ZoomFactor;

        if (device.HasTorch)
        {
            var torchMode = controls.TorchEnabled
                ? AVCaptureTorchMode.On
                : AVCaptureTorchMode.Off;
            if (device.IsTorchModeSupported(torchMode))
                device.TorchMode = torchMode;
        }

        if (applyFocus && device.FocusPointOfInterestSupported)
        {
            device.FocusPointOfInterest = controls.FocusPoint.HasValue
                ? GetDeviceFocusPoint(controls.FocusPoint.Value)
                : new CGPoint(0.5, 0.5);
        }

        if (applyFocus && controls.FocusMode.HasValue)
        {
            var focusMode = controls.FocusMode == CameraFocusMode.Single
                ? AVCaptureFocusMode.AutoFocus
                : AVCaptureFocusMode.ContinuousAutoFocus;
            if (device.IsFocusModeSupported(focusMode))
                device.FocusMode = focusMode;
        }

        if (controls.Capabilities.SupportsExposureCompensation)
            device.SetExposureTargetBias((float)controls.ExposureCompensation, null);
    }

    private void ApplyInitialControlsWithPreviewMapping()
    {
        if (_captureDevice is null)
            return;

        if (!_captureDevice.LockForConfiguration(out var configurationError))
        {
            var message = configurationError?.LocalizedDescription ??
                          "The initial camera controls could not be configured.";
            configurationError?.Dispose();
            throw new CameraPlatformException(new CameraFailure(
                CameraErrorCode.ControlConfigurationFailed,
                message,
                true,
                "DeviceControlLockFailed"));
        }

        try
        {
            ApplyControlsToDevice(_captureDevice, true);
        }
        finally
        {
            _captureDevice.UnlockForConfiguration();
            configurationError?.Dispose();
        }
    }

    private CGPoint GetDeviceFocusPoint(CameraPoint point)
    {
        if (_previewLayer is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return new CGPoint(point.X, point.Y);

        return _previewLayer.CaptureDevicePointOfInterestForPoint(
            new CGPoint(point.X * Bounds.Width, point.Y * Bounds.Height));
    }

    private void ConfigureOutput()
    {
        _captureDelegate = new VideoCaptureDelegate(
            (buffer, format, width, height, timestamp) =>
            {
                var callback = _frameCaptured;
                if (_isRunning && callback is not null)
                {
                    callback(
                        buffer,
                        format,
                        width,
                        height,
                        timestamp,
                        _effectiveConfiguration,
                        0,
                        _cameraOption == CameraOptions.Front);
                }
                else
                {
                    buffer.Release();
                }
            },
            ReportFailure,
            () => _minimumFrameInterval,
            () => _jpegQuality,
            () => _frameFormat,
            _rawFrameCapacity);
        _videoOutput = new AVCaptureVideoDataOutput
        {
            AlwaysDiscardsLateVideoFrames =
                _captureOptions.FrameDeliveryMode == CameraFrameDeliveryMode.Latest
        };

        var availablePixelFormats = _videoOutput.AvailableVideoCVPixelFormatTypes
            .ToArray();
        var pixelFormat = ResolvePixelFormat(
            _captureOptions.FrameFormat,
            availablePixelFormats,
            out _frameFormat);
        _capabilities = new CameraCaptureCapabilities(
            GetSupportedFrameFormats(availablePixelFormats),
            _availableCaptureResolutions,
            _availableFrameRateRanges);
        var settings = new CVPixelBufferAttributes
        {
            PixelFormatType = pixelFormat
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
        ConfigureConnection(
            _videoOutput.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()),
            false);
    }

    private static CVPixelFormatType ResolvePixelFormat(
        CameraFrameFormat requestedFormat,
        IReadOnlyCollection<CVPixelFormatType> availableFormats,
        out CameraFrameFormat effectiveFormat)
    {
        switch (requestedFormat)
        {
            case CameraFrameFormat.Jpeg:
                effectiveFormat = CameraFrameFormat.Jpeg;
                if (availableFormats.Contains(CVPixelFormatType.CV32BGRA))
                    return CVPixelFormatType.CV32BGRA;
                return ResolveYuvPixelFormat(availableFormats);
            case CameraFrameFormat.Native:
            case CameraFrameFormat.Yuv420:
                effectiveFormat = CameraFrameFormat.Yuv420;
                return ResolveYuvPixelFormat(availableFormats);
            case CameraFrameFormat.Bgra8888:
                if (!availableFormats.Contains(CVPixelFormatType.CV32BGRA))
                {
                    throw new CameraPlatformException(new CameraFailure(
                        CameraErrorCode.SessionConfigurationFailed,
                        "This iOS camera does not expose BGRA video frames.",
                        false,
                        "UnsupportedFrameFormat"));
                }
                effectiveFormat = CameraFrameFormat.Bgra8888;
                return CVPixelFormatType.CV32BGRA;
            default:
                throw new ArgumentOutOfRangeException(nameof(requestedFormat));
        }
    }

    private static CVPixelFormatType ResolveYuvPixelFormat(
        IReadOnlyCollection<CVPixelFormatType> availableFormats)
    {
        if (availableFormats.Contains(
                CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange))
            return CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange;
        if (availableFormats.Contains(
                CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange))
            return CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange;

        throw new CameraPlatformException(new CameraFailure(
            CameraErrorCode.SessionConfigurationFailed,
            "This iOS camera does not expose an 8-bit bi-planar YUV video format.",
            false,
            "UnsupportedFrameFormat"));
    }

    private static IEnumerable<CameraFrameFormat> GetSupportedFrameFormats(
        IReadOnlyCollection<CVPixelFormatType> availableFormats)
    {
        var supportsBgra = availableFormats.Contains(CVPixelFormatType.CV32BGRA);
        var supportsYuv = availableFormats.Contains(
                              CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange) ||
                          availableFormats.Contains(
                              CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange);
        if (supportsBgra || supportsYuv)
            yield return CameraFrameFormat.Jpeg;
        if (supportsYuv)
            yield return CameraFrameFormat.Yuv420;
        if (supportsBgra)
            yield return CameraFrameFormat.Bgra8888;
    }

    private void ConfigureConnection(AVCaptureConnection connection, bool isPreview)
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
            connection.VideoMirrored = isPreview
                ? _effectiveControls?.IsPreviewMirrored == true
                : _cameraOption == CameraOptions.Front;
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
        Action<CameraFrameBuffer, CameraFrameFormat, int, int, DateTimeOffset> frameCaptured,
        Action<CameraFailure> captureFailed,
        Func<TimeSpan> minimumFrameInterval,
        Func<int> jpegQuality,
        Func<CameraFrameFormat> frameFormat,
        RawFrameCapacity rawFrameCapacity)
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
                var imageBuffer = sampleBuffer.GetImageBuffer();
                if (imageBuffer is null)
                    return;

                var pixelBuffer = imageBuffer as CVPixelBuffer;
                if (pixelBuffer is null)
                {
                    imageBuffer.Dispose();
                    return;
                }

                var width = (int)pixelBuffer.Width;
                var height = (int)pixelBuffer.Height;
                var format = frameFormat();
                if (format == CameraFrameFormat.Jpeg)
                {
                    using (imageBuffer)
                    using (var image = new CIImage(imageBuffer))
                    using (var cgImage = _imageContext.CreateCGImage(image, image.Extent))
                    {
                        if (cgImage is null)
                            return;

                        using var uiImage = new UIImage(cgImage);
                        using var imageData = uiImage.AsJPEG(
                            Math.Clamp(jpegQuality() / 100f, 0.01f, 1f));
                        if (imageData is not null)
                        {
                            frameCaptured(
                                new ManagedCameraFrameBuffer(imageData.ToArray()),
                                format,
                                width,
                                height,
                                DateTimeOffset.UtcNow);
                        }
                    }
                }
                else
                {
                    if (!rawFrameCapacity.TryAcquire())
                    {
                        imageBuffer.Dispose();
                        return;
                    }

                    IosPixelBufferFrameBuffer buffer;
                    try
                    {
                        buffer = new IosPixelBufferFrameBuffer(
                            sampleBuffer,
                            pixelBuffer,
                            format,
                            rawFrameCapacity.Release);
                    }
                    catch
                    {
                        rawFrameCapacity.Release();
                        imageBuffer.Dispose();
                        throw;
                    }
                    sampleBuffer = null;
                    frameCaptured(
                        buffer,
                        format,
                        width,
                        height,
                        DateTimeOffset.UtcNow);
                }
            }
            catch (System.Exception exception)
            {
                captureFailed(new CameraFailure(
                    CameraErrorCode.CaptureFailed,
                    "iOS could not deliver a camera frame.",
                    true,
                    exception.GetType().Name,
                    exception));
            }
            finally
            {
                sampleBuffer?.Dispose();
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

    private sealed class IosPixelBufferFrameBuffer : CameraFrameBuffer
    {
        private CMSampleBuffer _sampleBuffer;
        private CVPixelBuffer _pixelBuffer;
        private IntPtr _retainedSampleBufferHandle;
        private Action _releaseCapacity;
        private readonly IntPtr[] _addresses;
        private readonly CameraFramePlaneDescription[] _descriptions;

        internal IosPixelBufferFrameBuffer(
            CMSampleBuffer sampleBuffer,
            CVPixelBuffer pixelBuffer,
            CameraFrameFormat format,
            Action releaseCapacity)
        {
            _sampleBuffer = sampleBuffer ??
                throw new ArgumentNullException(nameof(sampleBuffer));
            _pixelBuffer = pixelBuffer ??
                throw new ArgumentNullException(nameof(pixelBuffer));
            _releaseCapacity = releaseCapacity ??
                throw new ArgumentNullException(nameof(releaseCapacity));
            _retainedSampleBufferHandle = sampleBuffer.Handle;
            SafeRetain(_retainedSampleBufferHandle);

            var isLocked = false;
            try
            {
                var status = pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
                if (status != CVReturn.Success)
                    throw new InvalidOperationException(
                        $"Unable to lock CVPixelBuffer: {status}.");
                isLocked = true;

                if (pixelBuffer.IsPlanar)
                {
                    var planeCount = checked((int)pixelBuffer.PlaneCount);
                    _addresses = new IntPtr[planeCount];
                    _descriptions = new CameraFramePlaneDescription[planeCount];
                    for (var index = 0; index < planeCount; index++)
                    {
                        var planeIndex = new IntPtr(index);
                        var width = checked((int)pixelBuffer.GetWidthOfPlane(planeIndex));
                        var height = checked((int)pixelBuffer.GetHeightOfPlane(planeIndex));
                        var rowStride = checked((int)pixelBuffer.GetBytesPerRowOfPlane(planeIndex));
                        _addresses[index] = pixelBuffer.GetBaseAddress(planeIndex);
                        _descriptions[index] = new CameraFramePlaneDescription(
                            checked(rowStride * height),
                            rowStride,
                            index == 0 ? 1 : 2,
                            width,
                            height);
                    }
                }
                else
                {
                    var width = checked((int)pixelBuffer.Width);
                    var height = checked((int)pixelBuffer.Height);
                    var rowStride = checked((int)pixelBuffer.BytesPerRow);
                    _addresses = [pixelBuffer.BaseAddress];
                    _descriptions =
                    [
                        new CameraFramePlaneDescription(
                            checked(rowStride * height),
                            rowStride,
                            format == CameraFrameFormat.Bgra8888 ? 4 : 1,
                            width,
                            height)
                    ];
                }
            }
            catch
            {
                if (isLocked)
                    pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
                SafeRelease(_retainedSampleBufferHandle);
                _retainedSampleBufferHandle = IntPtr.Zero;
                throw;
            }
        }

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
            var pixelBuffer = Interlocked.Exchange(ref _pixelBuffer, null);
            if (pixelBuffer is not null)
            {
                pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
                pixelBuffer.Dispose();
            }

            Interlocked.Exchange(ref _sampleBuffer, null)?.Dispose();
            try
            {
                SafeRelease(Interlocked.Exchange(
                    ref _retainedSampleBufferHandle,
                    IntPtr.Zero));
            }
            finally
            {
                Interlocked.Exchange(ref _releaseCapacity, null)?.Invoke();
            }
        }

        private static void SafeRetain(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
                CFRetain(handle);
        }

        private static void SafeRelease(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
                CFRelease(handle);
        }

        [DllImport(Constants.CoreFoundationLibrary)]
        private static extern IntPtr CFRetain(IntPtr handle);

        [DllImport(Constants.CoreFoundationLibrary)]
        private static extern void CFRelease(IntPtr handle);
    }

    private sealed class RawFrameCapacity(int maximum)
    {
        private int _count;

        internal bool TryAcquire()
        {
            while (true)
            {
                var count = Volatile.Read(ref _count);
                if (count >= maximum)
                    return false;
                if (Interlocked.CompareExchange(ref _count, count + 1, count) == count)
                    return true;
            }
        }

        internal void Release() => Interlocked.Decrement(ref _count);
    }
}
