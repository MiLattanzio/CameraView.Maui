# Changelog

All notable changes to CameraView.Maui are documented in this file.

The project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

When preparing a release, move these entries to a versioned section in the form:

```text
## [1.0.0] - YYYY-MM-DD
```
