using System.IO.Compression;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: CameraView.Maui.PackageValidation <package.nupkg> <expected-version>");
    return 2;
}

try
{
    var packagePath = Path.GetFullPath(args[0]);
    var expectedVersion = args[1];
    var symbolPackagePath = Path.ChangeExtension(packagePath, ".snupkg");

    var repositoryCommit = PackageValidator.ValidatePackage(packagePath, expectedVersion);
    PackageValidator.ValidateSymbols(symbolPackagePath, expectedVersion, repositoryCommit);

    Console.WriteLine(
        $"Validated package metadata, symbols, and Source Link for CameraView.Maui {expectedVersion}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Package validation failed: {exception.Message}");
    return 1;
}

internal static partial class PackageValidator
{
    private const string PackageId = "CameraView.Maui";
    private const string RepositoryUrl = "https://github.com/MiLattanzio/CameraView.Maui";
    private const string ReleaseNotesUrl =
        "https://github.com/MiLattanzio/CameraView.Maui/blob/master/docs/CHANGELOG.md";

    private static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    public static string ValidatePackage(string packagePath, string expectedVersion)
    {
        Require(File.Exists(packagePath), $"Package not found: {packagePath}");

        using var archive = ZipFile.OpenRead(packagePath);
        var metadata = ReadMetadata(archive);

        Require(ReadElement(metadata, "id") == PackageId, "Unexpected package ID.");
        Require(
            ReadElement(metadata, "version") == expectedVersion,
            $"Package version does not match {expectedVersion}.");
        Require(ReadElement(metadata, "authors") == "Mi Lattanzio", "Unexpected package author.");
        Require(ReadElement(metadata, "readme") == "README.md", "Package readme is not configured.");
        Require(
            ReadElement(metadata, "releaseNotes") == ReleaseNotesUrl,
            "Package release notes do not point to the changelog.");

        var license = GetElement(metadata, "license");
        Require(
            (string?)license.Attribute("type") == "expression" && license.Value == "MIT",
            "The package must use the MIT license expression.");

        var repository = GetElement(metadata, "repository");
        var commit = (string?)repository.Attribute("commit");
        Require((string?)repository.Attribute("type") == "git", "Repository type must be git.");
        Require((string?)repository.Attribute("url") == RepositoryUrl, "Unexpected repository URL.");
        Require(
            commit is not null && GitCommitRegex().IsMatch(commit),
            "Repository metadata does not contain a full Git commit.");

        var tags = ReadElement(metadata, "tags")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var requiredTag in new[] { "maui", "camera", "android", "ios" })
            Require(tags.Contains(requiredTag), $"Required package tag is missing: {requiredTag}");

        Require(
            archive.GetEntry("README.md") is not null,
            "README.md is not included at the package root.");

        var assemblies = archive.Entries
            .Where(entry => entry.Name == $"{PackageId}.dll" &&
                            entry.FullName.StartsWith("lib/", StringComparison.Ordinal))
            .Select(entry => entry.FullName)
            .ToArray();

        RequireTarget(assemblies, "net9.0-android");
        RequireTarget(assemblies, "net9.0-ios");
        Require(assemblies.Length == 2, "The package must contain exactly two target assemblies.");

        return commit!;
    }

    public static void ValidateSymbols(
        string symbolPackagePath,
        string expectedVersion,
        string repositoryCommit)
    {
        Require(
            File.Exists(symbolPackagePath),
            $"Symbol package not found: {symbolPackagePath}");

        using var archive = ZipFile.OpenRead(symbolPackagePath);
        var metadata = ReadMetadata(archive);
        Require(ReadElement(metadata, "id") == PackageId, "Unexpected symbol package ID.");
        Require(
            ReadElement(metadata, "version") == expectedVersion,
            "Symbol package version does not match the main package.");

        var pdbEntries = archive.Entries
            .Where(entry => entry.Name == $"{PackageId}.pdb" &&
                            entry.FullName.StartsWith("lib/", StringComparison.Ordinal))
            .ToArray();

        RequireTarget(pdbEntries.Select(entry => entry.FullName), "net9.0-android");
        RequireTarget(pdbEntries.Select(entry => entry.FullName), "net9.0-ios");
        Require(pdbEntries.Length == 2, "The symbol package must contain exactly two PDB files.");

        foreach (var pdbEntry in pdbEntries)
            ValidateSourceLink(pdbEntry, repositoryCommit);
    }

    private static XElement ReadMetadata(ZipArchive archive)
    {
        var nuspecEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Require(nuspecEntries.Length == 1, "The package must contain exactly one nuspec file.");

        using var stream = nuspecEntries[0].Open();
        var document = XDocument.Load(stream);
        return document.Root?.Elements().Single(element => element.Name.LocalName == "metadata")
               ?? throw new InvalidDataException("Package metadata is missing.");
    }

    private static XElement GetElement(XElement metadata, string name) =>
        metadata.Elements().SingleOrDefault(element => element.Name.LocalName == name)
        ?? throw new InvalidDataException($"Package metadata is missing '{name}'.");

    private static string ReadElement(XElement metadata, string name) =>
        GetElement(metadata, name).Value;

    private static void RequireTarget(IEnumerable<string> paths, string targetPrefix) =>
        Require(
            paths.Any(path => path.StartsWith($"lib/{targetPrefix}", StringComparison.Ordinal)),
            $"Package target is missing: {targetPrefix}");

    private static void ValidateSourceLink(
        ZipArchiveEntry pdbEntry,
        string repositoryCommit)
    {
        using var pdbStream = pdbEntry.Open();
        using var copy = new MemoryStream();
        pdbStream.CopyTo(copy);
        copy.Position = 0;

        using var provider = MetadataReaderProvider.FromPortablePdbStream(copy);
        var reader = provider.GetMetadataReader();
        var sourceLink = reader.CustomDebugInformation
            .Select(reader.GetCustomDebugInformation)
            .SingleOrDefault(info =>
                info.Parent.Kind == HandleKind.ModuleDefinition &&
                reader.GetGuid(info.Kind) == SourceLinkKind);

        Require(!sourceLink.Equals(default(CustomDebugInformation)),
            $"Source Link data is missing from {pdbEntry.FullName}.");

        using var json = JsonDocument.Parse(reader.GetBlobBytes(sourceLink.Value));
        Require(
            json.RootElement.TryGetProperty("documents", out var documents),
            $"Source Link documents are missing from {pdbEntry.FullName}.");

        var mappings = documents.EnumerateObject().ToArray();
        Require(mappings.Length > 0, $"Source Link has no mappings in {pdbEntry.FullName}.");

        foreach (var mapping in mappings)
        {
            var sourceUrl = mapping.Value.GetString();
            var expectedPrefix =
                $"https://raw.githubusercontent.com/MiLattanzio/CameraView.Maui/{repositoryCommit}/";
            Require(
                sourceUrl is not null &&
                sourceUrl.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
                sourceUrl.EndsWith("/*", StringComparison.Ordinal),
                $"Unexpected Source Link mapping in {pdbEntry.FullName}: {sourceUrl}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitCommitRegex();
}
