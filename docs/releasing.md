# Release and Trusted Publishing

The `publish.yml` workflow publishes packages to NuGet.org with GitHub OIDC and a short-lived API key. Do not create or store a long-lived NuGet API key.

## One-time GitHub configuration

1. Open the repository settings.
2. Create a GitHub Actions environment named exactly `nuget.org`.
3. Add an environment secret named `NUGET_USER`. Its value is the NuGet.org profile username that owns the package, not an email address.
4. Optionally add required reviewers to the environment to approve production releases.

## One-time NuGet.org configuration

Sign in to NuGet.org and add a Trusted Publishing policy with these GitHub values:

| Policy field | Value |
| --- | --- |
| Repository owner | `MiLattanzio` |
| Repository | `CameraView.Maui` |
| Workflow file | `publish.yml` |
| Environment | `nuget.org` |

Select the NuGet.org user or organization that owns `CameraView.Maui` as the policy owner. Enter only the workflow filename, not `.github/workflows/publish.yml`.

The policy and workflow environment must match exactly. No `NUGET_API_KEY` secret is required: `NuGet/login@v1` exchanges GitHub's OIDC token for a temporary key immediately before publishing.

## Prepare a release

1. Move relevant entries from `Unreleased` in [CHANGELOG.md](CHANGELOG.md) to a dated version heading.
2. Update `<Version>` in `CameraView.Maui/CameraView.Maui.csproj` to the same version.
3. Ensure CI succeeds on `master`.
4. Create and push an annotated SemVer tag:

```shell
git tag -a v1.3.1 -m "CameraView.Maui 1.3.1"
git push origin v1.3.1
```

5. Create and publish the GitHub release for that tag. A tag push by itself never publishes to NuGet.org.

The workflow starts only for the `release.published` event. It validates that the release tag points to a commit contained in `master` and that the tag version matches the project version. It then builds every .NET 9/.NET 10 Android/iOS target, validates API compatibility against the previous stable package, creates and inspects `.nupkg` and `.snupkg` artifacts, builds a clean package consumer, obtains the temporary credential, and publishes both package and symbols.

Package inspection verifies the NuGet ID and version, license, readme, release-notes URL, repository commit, target assemblies, Portable PDBs, and Source Link mappings before authentication is requested.

NuGet packages are immutable. The publish workflow cannot be started by a branch push, a tag push, or a manual dispatch. If a version was already published, fix the issue and release a new version.

## Package artifacts

Every CI run retains an unpublished, validated package for 14 days. Release workflows retain the exact published `.nupkg` and `.snupkg` files for 30 days.
