# Roadmap

This roadmap describes the planned direction after CameraView.Maui 1.0.0. It is a living plan: release order expresses priority, while dates are intentionally assigned only when implementation work starts.

Public member names shown below are provisional until their implementation is reviewed.

## Release principles

- Releases in the `1.x` line remain source- and binary-compatible with 1.0.0.
- Android and iOS reach feature parity before a feature is considered complete.
- Existing defaults continue to produce the current 720p-or-lower JPEG stream unless an application opts into new settings.
- .NET 10 support is added before .NET 9 is retired. Dropping a target framework is reserved for a major release.
- Additive opt-in raw frames and explicit buffer leases may extend the existing 1.2 capture-configuration theme without changing the JPEG default. Removing compatibility APIs remains reserved for 2.0.
- Patch releases remain backward compatible and may complete an already shipped minor capability with additive opt-in APIs; independently scoped public features ship in minor releases.

## Planned releases

| Version | Theme | Status |
| --- | --- | --- |
| 1.0.1 | Reliability and packaging hardening | Released 2026-08-06 |
| 1.1.0 | .NET 10 and camera diagnostics | Released 2026-08-06 |
| 1.2.0 | Capture configuration and frame metadata | GitHub release 2026-08-06; not published to NuGet.org |
| 1.2.1 | Configuration and preview hardening | Released 2026-08-07 |
| 1.2.2 | High-throughput configurable frame pipeline | Released 2026-08-07 |
| 1.3.0 | Interactive camera controls | Released 2026-08-07 |
| 1.3.1 | Android preview rendering hardening | Released 2026-08-08 |
| 1.4.0 | High-quality still photo capture | Planned |
| 2.0.0 | Async and zero-copy frame pipeline | Exploration |

## 1.0.1 — Reliability and packaging hardening

This is a maintenance release with no new public API.

Delivered scope:

- Fix regressions found after the first NuGet release, prioritizing lifecycle, permission, and device-specific camera failures.
- Add package-install and sample-build smoke tests to CI.
- Add API compatibility checks so patch releases cannot accidentally change the public contract.
- Improve diagnostic guidance with known device and operating-system behavior.
- Keep NuGet metadata, symbols, Source Link, changelog, and release notes verified by the publishing workflow.

Exit criteria:

- The packed artifact installs into a clean MAUI sample.
- Android and iOS Release builds pass from the produced package, not only through a project reference.
- No known critical preview, resume, or resource-release regression remains open.

## 1.1.0 — .NET 10 and camera diagnostics

The first minor release makes failures observable and moves consumers toward the current LTS toolchain without abandoning .NET 9 applications.

Delivered scope:

- Add .NET 10 Android and iOS targets while retaining the .NET 9 targets in the same package.
- Add a camera state model such as `Stopped`, `Starting`, `Running`, `Suspended`, `PermissionDenied`, and `Failed`.
- Add `StateChanged` and structured `ErrorOccurred` notifications with stable error codes.
- Distinguish permission denial, unavailable hardware, camera-in-use, session-configuration, and unexpected native errors.
- Expose the currently selected camera and the actual running state without requiring applications to infer them from frame delivery.
- Extend the sample and documentation with state-driven UI and recovery behavior.

Exit criteria:

- A single NuGet package works from both supported .NET target lines.
- Screen lock, background/resume, denied permission, camera switching, and unavailable-camera paths produce deterministic state transitions.
- Native errors are reported to the application and never escape the capture callback.

## 1.2.0 — Capture configuration and frame metadata

This release lets applications balance image quality, throughput, memory use, and processing latency.

Implementation scope:

- Add preferred capture resolution or resolution presets, with documented hardware negotiation.
- Add configurable JPEG quality.
- Add a maximum frame-rate or minimum-frame-interval option.
- Add frame width, height, timestamp, orientation, camera position, and sequence number to successful results.
- Expose the effective configuration selected by the native camera when the requested values are unavailable.
- Preserve the 1.0 behavior as the default configuration.

Exit criteria:

- Configuration changes restart the session safely and survive application resume.
- Unsupported requests fall back predictably and report the effective settings.
- Frame throttling does not create an unbounded queue and is verified under sustained processing load.

## 1.2.1 — Configuration and preview hardening

This maintenance release finalizes the 1.2 capture API and publishes it to NuGet.org after the 1.2.0 package was blocked by compatibility validation.

Delivered scope:

- Replace independently mutable settings with one immutable, atomic `CameraCaptureOptions` value.
- Support arbitrary resolutions and explicit `Closest`, `AtMost`, `AtLeast`, and `Exact` negotiation policies.
- Report requested and effective capture settings, including predictable native fallbacks.
- Restore the original `CameraResult(byte[])` constructor required for binary compatibility with 1.1.0 consumers.
- Preserve the Android preview aspect ratio across portrait, landscape, reverse portrait, and reverse landscape rotations.
- Make the sample application deployable from Rider while retaining an explicit .NET 9 compatibility override.
- Validate configuration selection and preview transformation calculations in CI before packing.

Exit criteria:

- Package validation reports no breaking changes against the latest public NuGet version, 1.1.0.
- Android and iOS builds pass for .NET 9 and .NET 10.
- The Android sample remains active and undistorted across all four display rotations.
- Trusted Publishing completes and `CameraView.Maui` 1.2.1 is visible on NuGet.org.

## 1.2.2 — High-throughput configurable frame pipeline

This patch completes the 1.2 capture-configuration work without changing the existing JPEG behavior or removing any public API.

Implementation scope:

- Keep `CameraCaptureOptions.Default` and `OnFrameResult` byte-for-byte compatible with the JPEG pipeline shipped in 1.2.1.
- Add an opt-in `FrameAvailable` API for encoded JPEG, native YUV, and supported BGRA buffers.
- Expose zero-copy frame planes with dimensions, row stride, pixel stride, rotation, and mirroring metadata.
- Use explicit borrowed-frame lifetime and retainable disposable leases for asynchronous processing.
- Add latest-frame and sequential delivery policies with a bounded native buffer capacity.
- Negotiate the platform default, maximum, or closest requested native frame-rate range independently from managed delivery throttling.
- Report concrete formats, supported resolutions, frame-rate ranges, and the effective native configuration.
- Add a `Realtime` profile for low-latency 720p native YUV delivery at the fastest supported native rate.

Exit criteria:

- Existing 1.2.1 consumers pass package API and binary compatibility validation unchanged.
- Raw delivery performs no JPEG encode/decode and no plane copy before the subscriber reads `CameraFramePlane.Span`.
- Retained Android `Image` and iOS `CVPixelBuffer` resources are released deterministically after the final frame lease.
- Android and iOS build for .NET 9 and .NET 10, and the packed package compiles in a clean consumer.
- Documentation covers plane layouts, stride, rotation, ownership, backpressure, and asynchronous processing.

## 1.3.0 — Interactive camera controls

This release adds controls required by scanner, document-capture, and assisted-photography experiences.

Implementation scope:

- Add atomic `CameraControlOptions` that update the active session without a capture restart and survive camera switching or resume.
- Add zoom with reported native minimum and maximum factors, including Android zoom-ratio/crop fallback.
- Add torch control that falls back to off when the selected camera has no flash unit.
- Add continuous or single autofocus with an optional point normalized against the visible preview.
- Add exposure compensation in EV, clamped and quantized to the device-supported range and step.
- Report applied values, native ranges, focus modes, focus-point support, and deterministic fallback flags through `EffectiveControls`.
- Configure preview mirroring independently while preserving the encoded/raw frame behavior reported by `CameraFrame.IsMirrored`.

Exit criteria:

- Controls behave consistently across camera switching, rotation, background/resume, and unsupported hardware.
- Values are clamped or rejected deterministically according to the documented contract.
- Physical-device tests cover at least one supported Android device and one supported iOS device.

## 1.3.1 — Android preview rendering hardening

This maintenance release fixes device-specific viewfinder distortion without changing the public API or frame-processing contract.

Delivered scope:

- Decouple Android's preview stream from the selected capture resolution and frame format.
- Use one stable 720p-or-closest Camera2 preview output across default, low-bandwidth, balanced, custom, and raw profiles.
- Replace device-dependent `TextureView` matrix correction with an aspect-preserving `SurfaceView` layout.
- Apply one uniform aspect-fill scale and crop only the overflowing dimension across display rotations.
- Add regression tests for portrait, landscape, mirroring, and profile transitions.

Exit criteria:

- Preview objects retain their proportions across capture profiles and device rotations on the Android reference device.
- Screen lock and application resume recreate the native preview surface without a black viewfinder.
- The package remains API- and binary-compatible with public version 1.3.0.

## 1.4.0 — High-quality still photo capture

The streaming callback is optimized for analysis; this release adds a separate path for user-initiated photography.

Planned scope:

- Add an asynchronous `CapturePhotoAsync(CancellationToken)` operation.
- Capture at a higher supported resolution than the analysis stream without requiring a permanent high-bandwidth frame feed.
- Return photo bytes together with dimensions, timestamp, orientation, and camera metadata.
- Coordinate still capture with preview and frame delivery without leaking or deadlocking the native session.
- Add save, share, and thumbnail examples while leaving persistent-storage policy to the application.

Exit criteria:

- Cancellation, concurrent calls, lifecycle suspension, and camera switching have documented deterministic outcomes.
- The live preview recovers after success, cancellation, and native capture failure.
- The API does not require storage or image-processing dependencies.

## 2.0.0 — Async frame pipeline and API cleanup

Version 2.0 is reserved for changes that cannot be introduced while preserving the 1.x contract.

Exploration scope:

- Replace the mutable `CameraResult` contract with immutable frame, photo, state, and error types.
- Build an asynchronous processing API over the 1.2.2 JPEG/raw formats and explicit buffer leases.
- Evaluate pooled managed copies for processors that cannot consume native plane memory synchronously.
- Provide a built-in latest-frame asynchronous processor with bounded backpressure and cancellation.
- Add explicit asynchronous start and stop operations.
- Remove the legacy bindable-property aliases and make handler-only result methods non-public.
- Rename .NET events according to standard conventions while providing a migration guide.
- Target supported LTS tooling only; additional platforms require their own support proposal and test matrix.

2.0 implementation should begin only after 1.2.2 telemetry and consumer feedback establish which asynchronous, pooling, and migration APIs are actually needed.

## Release gate for every version

Every release must satisfy all of the following:

1. Public API review and compatibility report.
2. CI build, pack, and clean-sample package installation.
3. Android and iOS lifecycle and resource-release smoke tests.
4. Updated README, API reference, samples, and changelog.
5. A green Trusted Publishing workflow and verification of the public NuGet package.

## Feedback and prioritization

Critical crashes, camera resource leaks, black previews after resume, and package-consumption failures take precedence over roadmap features. Other requests are evaluated by cross-platform feasibility, performance cost, API stability, and the amount of application code they remove.
