# Contributing

Contributions and focused bug reports are welcome.

## Development requirements

- .NET SDK 10.0.302 or a compatible servicing update capable of building the .NET 9 and .NET 10 Android/iOS targets.
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

Run deterministic capture-configuration tests:

```shell
dotnet run \
  --project tests/CameraView.Maui.ConfigurationTests/CameraView.Maui.ConfigurationTests.csproj \
  --configuration Release
```

Create a local package:

```shell
dotnet pack CameraView.Maui/CameraView.Maui.csproj \
  --configuration Release \
  --output artifacts/nuget
```

Packing runs the .NET SDK package validator against the previous stable NuGet baseline. CI also exercises deterministic option validation and resolution negotiation, inspects the generated package and symbols, then builds `tests/CameraView.Maui.PackageSmokeTest` against the local package artifact for .NET 9 and .NET 10 on Android and iOS.

## Pull requests

1. Keep changes focused and avoid committing IDE, `bin`, `obj`, or package artifacts.
2. Update user documentation for public API or behavior changes.
3. Add an entry under `Unreleased` in [CHANGELOG.md](CHANGELOG.md).
4. Build the .NET 9 and .NET 10 Android/iOS targets.
5. Describe device or emulator validation for camera and lifecycle changes.

CI compiles the library, validates its public API and package metadata, builds an Android/iOS package consumer, and creates an unpublished package artifact.
