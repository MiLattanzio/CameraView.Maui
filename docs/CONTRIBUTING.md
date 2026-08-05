# Contributing

Contributions and focused bug reports are welcome.

## Development requirements

- A .NET SDK capable of building the `net9.0-android` and `net9.0-ios` targets.
- The .NET MAUI Android and iOS workloads.
- Android SDK for Android builds.
- macOS and Xcode for full iOS device validation.

Restore workloads and dependencies:

```shell
dotnet workload restore CameraView.Maui.sln
dotnet restore CameraView.Maui.sln
```

Build the solution:

```shell
dotnet build CameraView.Maui.sln --configuration Release
```

Create a local package:

```shell
dotnet pack CameraView.Maui/CameraView.Maui.csproj \
  --configuration Release \
  --output artifacts/nuget
```

## Pull requests

1. Keep changes focused and avoid committing IDE, `bin`, `obj`, or package artifacts.
2. Update user documentation for public API or behavior changes.
3. Add an entry under `Unreleased` in [CHANGELOG.md](CHANGELOG.md).
4. Build Android and iOS targets.
5. Describe device or emulator validation for camera and lifecycle changes.

CI compiles the library and test application and creates an unpublished package artifact.
