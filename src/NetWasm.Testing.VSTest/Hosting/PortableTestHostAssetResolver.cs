using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed class PortableTestHostAssetResolver : IPortableTestHostAssetResolver
{
    internal const string TestPlatformVersion = "18.0.1";
    private const string TestHostDirectoryName = "testhost";
    private const string ManifestFileName = "testhost.manifest.json";
    private const string TestHostFileName = "testhost.dll";
    private const string DependencyManifestFileName = "testhost.deps.json";
    private const string RuntimeConfigurationFileName = "testhost.runtimeconfig.json";
    private const string PackageDirectoryName = "packages";
    private static readonly string[] ExpectedFiles =
    [
        "Microsoft.TestPlatform.CommunicationUtilities.dll",
        "Microsoft.TestPlatform.CoreUtilities.dll",
        "Microsoft.TestPlatform.CrossPlatEngine.dll",
        "Microsoft.TestPlatform.PlatformAbstractions.dll",
        "Microsoft.TestPlatform.Utilities.dll",
        "Microsoft.VisualStudio.TestPlatform.Common.dll",
        "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll",
        "packages/newtonsoft.json/13.0.3/lib/net6.0/Newtonsoft.Json.dll",
        DependencyManifestFileName,
        TestHostFileName,
        RuntimeConfigurationFileName,
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };
    private readonly string _extensionRoot;

    internal PortableTestHostAssetResolver(string extensionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionRoot);
        if (!IsCanonicalFullyQualifiedPath(extensionRoot))
        {
            throw new ArgumentException(
                "The VSTest extension root must be a canonical fully qualified path.",
                nameof(extensionRoot));
        }
        _extensionRoot = extensionRoot;
    }

    public PortableTestHostAssets Resolve()
    {
        var root = Path.GetFullPath(Path.Combine(_extensionRoot, TestHostDirectoryName));
        var manifestPath = Path.Combine(root, ManifestFileName);
        PortableTestHostManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PortableTestHostManifest>(
                File.ReadAllBytes(manifestPath),
                JsonOptions)
                ?? throw new InvalidDataException();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidDataException(
                "The packaged NetWasm portable testhost manifest is missing or invalid.",
                exception);
        }

        ValidateManifest(manifest, root);
        var testHostPath = Path.Combine(root, TestHostFileName);
        var dependencyManifestPath = Path.Combine(root, DependencyManifestFileName);
        var runtimeConfigurationPath = Path.Combine(root, RuntimeConfigurationFileName);
        return new PortableTestHostAssets(
            testHostPath,
            dependencyManifestPath,
            runtimeConfigurationPath,
            Path.Combine(root, PackageDirectoryName));
    }

    private static void ValidateManifest(PortableTestHostManifest manifest, string root)
    {
        if (manifest.SchemaVersion != 1
            || !string.Equals(
                manifest.TestPlatformVersion,
                TestPlatformVersion,
                StringComparison.Ordinal)
            || manifest.Files.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The packaged NetWasm portable testhost identity is unsupported.");
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            if (file is null
                || string.IsNullOrWhiteSpace(file.RelativePath)
                || Path.IsPathFullyQualified(file.RelativePath)
                || file.RelativePath.Contains('\\', StringComparison.Ordinal)
                || file.RelativePath.Split('/').Any(segment => segment is "" or "." or "..")
                || string.IsNullOrWhiteSpace(file.Sha256)
                || file.Sha256.Length != 64
                || !file.Sha256.All(char.IsAsciiHexDigitLower)
                || !paths.Add(file.RelativePath))
            {
                throw new InvalidDataException(
                    "The packaged NetWasm portable testhost manifest contains an invalid file identity.");
            }

            var path = Path.GetFullPath(
                Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(path)
                || !string.Equals(Hash(path), file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The packaged NetWasm portable testhost closure failed integrity validation.");
            }
        }
        if (!paths.SetEquals(ExpectedFiles))
        {
            throw new InvalidDataException(
                "The packaged NetWasm portable testhost manifest has an incomplete or unexpected file set.");
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsCanonicalFullyQualifiedPath(string path)
    {
        if (path.Contains('\0', StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        return string.Equals(Path.GetFullPath(path), path, PathComparison);
    }
}
