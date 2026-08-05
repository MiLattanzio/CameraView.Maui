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
        Action<byte[]> frameCaptured)
    {
        if (_isRunning &&
            _cameraOption == cameraOption &&
            _orientation == orientation)
        {
            _frameCaptured = frameCaptured;
            return;
        }

        Stop();

        _cameraOption = cameraOption;
        _orientation = orientation;
        _frameCaptured = frameCaptured;
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

        _isRunning = true;
        _captureSession.StartRunning();
    }

    public void Stop()
    {
        _isRunning = false;
        _frameCaptured = null;

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
            ?? throw new InvalidOperationException($"No {cameraOption} camera was found.");

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
            inputError?.Dispose();
            throw new InvalidOperationException(message);
        }
        inputError?.Dispose();

        if (!_captureSession.CanAddInput(input))
        {
            input.Dispose();
            throw new InvalidOperationException("The camera input cannot be added to the capture session.");
        }

        _captureSession.AddInput(input);
    }

    private void ConfigureOutput()
    {
        _captureDelegate = new VideoCaptureDelegate(EmitFrame);
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
            throw new InvalidOperationException("The camera output cannot be added to the capture session.");

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

    private sealed class VideoCaptureDelegate(Action<byte[]> frameCaptured)
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
