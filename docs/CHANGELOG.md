# Changelog

All notable changes to CameraView.Maui are documented in this file.

The project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/MiLattanzio/CameraView.Maui/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/MiLattanzio/CameraView.Maui/releases/tag/v1.0.0
