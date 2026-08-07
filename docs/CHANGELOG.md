# Changelog

All notable changes to CameraView.Maui are documented in this file.

The project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Opt-in `FrameAvailable` delivery for JPEG, native YUV, and supported BGRA camera buffers.
- Zero-copy `CameraFramePlane.Span` access with native plane dimensions, row stride, and pixel stride.
- Borrowed event frames plus explicit retainable, disposable `CameraFrame` leases for asynchronous processing.
- `Latest` and `Sequential` native delivery modes with configurable outstanding-buffer capacity.
- Native frame-rate negotiation using platform default, maximum, or closest-target modes, independently from managed delivery throttling.
- Frame rotation and mirroring metadata for processing raw sensor output correctly.
- Effective camera capabilities covering concrete formats, output resolutions, and supported native frame-rate ranges.
- A `CameraCaptureOptions.Realtime` profile for maximum-rate 720p native YUV analysis.
- Atomic live `CameraControlOptions` for zoom, torch, normalized focus points, focus mode, exposure compensation, and preview mirroring.
- `EffectiveControls` and `EffectiveControlsChanged` reporting clamped values, native ranges, supported focus modes, torch availability, and deterministic fallbacks.
- Camera2 zoom-ratio/crop, AF/AE metering regions, autofocus triggers, exposure compensation, and torch integration on Android.
- AVFoundation zoom, focus point/mode, exposure bias, torch, and independent preview mirroring integration on iOS.

### Changed

- Package version advanced to 1.3.0 and API compatibility continues to validate against public version 1.2.1.
- Android can stream direct `YUV_420_888` planes without JPEG encoding or a managed pixel copy.
- iOS can stream locked NV12 or BGRA `CVPixelBuffer` planes without Core Image/UIKit conversion.
- The sample application can switch between the compatibility JPEG profiles and the realtime raw profile.
- `CameraCaptureOptions.Default` and `OnFrameResult` retain the 1.2.1 JPEG behavior.
- Interactive control changes update the active native request/device without restarting the capture session and are reapplied after camera switching or resume.
- The sample application includes live zoom and exposure sliders, torch, tap-to-focus, focus reset, and preview-mirroring controls.

### Fixed

- NuGet Trusted Publishing now runs only when a GitHub release is published, avoiding the duplicate run previously caused by both the release and its tag push.

## [1.2.1] - 2026-08-07

### Added

- Atomic immutable `CameraCaptureOptions` with reusable low-bandwidth, balanced, and high-quality profiles.
- Preset and arbitrary capture resolutions with `Closest`, `AtMost`, `AtLeast`, and `Exact` selection policies.
- Optional JPEG quality plus combinable fractional maximum-frame-rate and minimum-frame-interval limits.
- Effective capture/preview configuration snapshots, fallback detection, and change notifications.
- Deterministic tests for capture negotiation and Android preview transformations.

### Changed

- A complete capture configuration now causes one native restart and is retained across resume.
- Resolution fallback considers both aspect ratio and pixel count instead of pixel count alone.
- The default leaves Android JPEG quality to the platform and preserves the 1.0 720p-or-lower behavior.
- Android selects a preview size matching the capture aspect ratio; iOS selects an actual device format instead of assuming preset dimensions.
- Frame throttling uses the strictest requested interval and drops before managed delivery without creating a queue.
- Package compatibility is checked against the previous public version 1.1.0.
- The sample app selects one .NET version per platform by default so Rider can deploy it without an ambiguous Android target framework.

### Fixed

- Restored binary compatibility for the original `CameraResult(byte[])` constructor.
- Stale native configuration callbacks can no longer overwrite a newer effective configuration.
- Android drains and closes throttled `ImageReader` frames instead of allowing producer backpressure.
- Android preview scaling now preserves the camera aspect ratio across all display rotations instead of stretching the image.
- NuGet publishing now also starts when a GitHub release is published, including releases whose tag is created by GitHub.

## [1.2.0] - 2026-08-06

The GitHub release was created, but NuGet publishing was blocked by API compatibility validation. The finalized API and fixes are published in 1.2.1.

### Added

- Configurable capture resolution presets (`Default`, `Qvga`, `Vga`, `Hd720p`, and `Hd1080p`).
- Configurable JPEG quality, maximum frame rate, and minimum frame interval.
- Frame width, height, UTC timestamp, orientation, camera position, and sequence number.
- `EffectiveConfiguration` reporting the values negotiated by the native camera.

### Changed

- Configuration changes safely restart the native session and are retained across resume.
- Android uses the latest available image and drops frames before delivery when throttling is enabled; iOS continues to discard late video frames.

## [1.1.0] - 2026-08-06

### Added

- .NET 10 Android and iOS targets alongside the existing .NET 9 targets.
- Read-only `State` and `IsRunning` properties for the actual native camera lifecycle.
- Dispatcher-based `StateChanged` notifications with previous state and selected camera.
- Structured `ErrorOccurred` notifications with stable cross-platform codes, native diagnostics, recoverability, and exceptions.
- Native Camera2 and AVFoundation error, disconnection, runtime-error, and interruption reporting.
- State-driven diagnostics in the sample application and package smoke test.

### Changed

- CI and publishing now use .NET SDK 10.0.302 and validate four Android/iOS target assemblies and symbol files.
- Package compatibility is checked against version 1.0.1.
- Exceptions from frame, state, and error subscribers are isolated and written to debug output instead of escaping into native capture callbacks.

### Fixed

- `Running` is reported only after the native repeating request or capture session has actually started.
- Stale native callbacks cannot change state after a newer configuration or lifecycle transition.

## [1.0.1] - 2026-08-06

### Added

- A versioned roadmap for the planned 1.x releases and the 2.0 frame-pipeline redesign.
- CI smoke tests that restore and build an Android/iOS MAUI consumer from the generated NuGet package.
- Automated validation of package metadata, symbol packages, Portable PDBs, and Source Link mappings.

### Changed

- NuGet packaging now checks binary and target-framework compatibility against version 1.0.0.
- Release publishing rejects a version that does not match the project version.
- Troubleshooting documentation now covers permission resets, camera contention, platform logs, and issue diagnostics.

## [1.0.0] - 2026-08-05

### Added

- Native Camera2 preview and JPEG frame capture on Android.
- Native AVFoundation preview and JPEG frame capture on iOS.
- Front/rear camera selection and portrait/landscape orientation.
- MAUI handler registration through `UseCameraView`.
- Runtime camera permission handling.
- GitHub Actions CI, package artifacts, symbols, and NuGet.org Trusted Publishing.
- User, contributor, troubleshooting, and release documentation.

### Fixed

- Corrected Android/iOS handler namespace resolution and XAML namespace usage.
- Camera sessions are released on app deactivation and restored after screen unlock or resume.

[Unreleased]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.2.1...HEAD
[1.2.1]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/MiLattanzio/CameraView.Maui/releases/tag/v1.0.0
