# Changelog

All notable changes to CameraView.Maui are documented in this file.

The project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-08-06

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

[Unreleased]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/MiLattanzio/CameraView.Maui/releases/tag/v1.0.0
