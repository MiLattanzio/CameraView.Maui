# Roadmap

This roadmap describes the planned direction after CameraView.Maui 1.0.0. It is a living plan: release order expresses priority, while dates are intentionally assigned only when implementation work starts.

Public member names shown below are provisional until their implementation is reviewed.

## Release principles

- Releases in the `1.x` line remain source- and binary-compatible with 1.0.0.
- Android and iOS reach feature parity before a feature is considered complete.
- Existing defaults continue to produce the current 720p-or-lower JPEG stream unless an application opts into new settings.
- .NET 10 support is added before .NET 9 is retired. Dropping a target framework is reserved for a major release.
- Raw frames, explicit buffer ownership, and removal of compatibility APIs are reserved for 2.0.
- Patch releases contain fixes, documentation, and packaging changes only; new public features ship in minor releases.

## Planned releases

| Version | Theme | Status |
| --- | --- | --- |
| 1.0.1 | Reliability and packaging hardening | Released 2026-08-06 |
| 1.1.0 | .NET 10 and camera diagnostics | Next |
| 1.2.0 | Capture configuration and frame metadata | Planned |
| 1.3.0 | Interactive camera controls | Planned |
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

Planned scope:

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

Planned scope:

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

## 1.3.0 — Interactive camera controls

This release adds controls required by scanner, document-capture, and assisted-photography experiences.

Planned scope:

- Zoom with reported minimum and maximum factors.
- Torch control when the selected camera supports it.
- Tap-to-focus or an explicit normalized focus point.
- Exposure compensation within the device-supported range.
- Capability reporting so applications can enable only supported controls.
- Independent preview mirroring for the front camera while keeping encoded output behavior explicit.

Exit criteria:

- Controls behave consistently across camera switching, rotation, background/resume, and unsupported hardware.
- Values are clamped or rejected deterministically according to the documented contract.
- Physical-device tests cover at least one supported Android device and one supported iOS device.

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

## 2.0.0 — Async and zero-copy frame pipeline

Version 2.0 is reserved for changes that cannot be introduced while preserving the 1.x contract.

Exploration scope:

- Replace the mutable `CameraResult` contract with immutable frame, photo, state, and error types.
- Support encoded JPEG and platform-neutral raw pixel formats.
- Introduce explicit disposable or pooled buffer ownership to reduce per-frame allocations.
- Provide a built-in latest-frame asynchronous processor with bounded backpressure and cancellation.
- Add explicit asynchronous start and stop operations.
- Remove the legacy bindable-property aliases and make handler-only result methods non-public.
- Rename .NET events according to standard conventions while providing a migration guide.
- Target supported LTS tooling only; additional platforms require their own support proposal and test matrix.

2.0 implementation should begin only after 1.x telemetry, issues, and consumer feedback establish which raw formats and ownership model are actually needed.

## Release gate for every version

Every release must satisfy all of the following:

1. Public API review and compatibility report.
2. CI build, pack, and clean-sample package installation.
3. Android and iOS lifecycle and resource-release smoke tests.
4. Updated README, API reference, samples, and changelog.
5. A green Trusted Publishing workflow and verification of the public NuGet package.

## Feedback and prioritization

Critical crashes, camera resource leaks, black previews after resume, and package-consumption failures take precedence over roadmap features. Other requests are evaluated by cross-platform feasibility, performance cost, API stability, and the amount of application code they remove.
