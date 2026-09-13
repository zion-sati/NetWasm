using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Versioning;

namespace NetWasm.Sdk.Pack.Restore;

public sealed class FileRestoreEvidenceReader : IRestoreEvidenceReader
{
    private readonly IRestoreEvidenceValidator validator;

    public FileRestoreEvidenceReader(IRestoreEvidenceValidator validator)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public RestoreEvidence Read(RestoreEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        validator.Validate(evidence);
        if (!evidence.Required && string.IsNullOrWhiteSpace(evidence.AssetsFilePath))
        {
            return evidence;
        }

        var assetsHash = HashFile(evidence.AssetsFilePath);
        if (!string.Equals(assetsHash, evidence.AssetsFileHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The restore assets file changed after restore.");
        }

        JsonDocument? lockDocument = null;
        if (!string.IsNullOrWhiteSpace(evidence.LockFilePath))
        {
            var lockHash = HashFile(evidence.LockFilePath);
            if (!string.Equals(lockHash, evidence.LockFileHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The restore lock file changed after restore.");
            }

            lockDocument = ReadJson(evidence.LockFilePath, "The restore lock file is unreadable or malformed.");
        }

        using (lockDocument)
        {
            var parsed = ParseAssets(evidence.AssetsFilePath);
            ValidateLockParity(lockDocument, parsed, evidence.TargetKeys);
            if (!evidence.TargetKeys.Order(StringComparer.Ordinal).SequenceEqual(parsed.TargetKeys, StringComparer.Ordinal))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK005, "The restored target keys differ from the requested targets.");
            }

            if (!evidence.Graph.IsDefaultOrEmpty && !SameGraph(evidence.Graph, parsed.Graph))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The supplied restore graph does not match project.assets.json.");
            }

            return evidence with { TargetKeys = parsed.TargetKeys, Graph = parsed.Graph };
        }
    }

    private static (ImmutableArray<string> TargetKeys, ImmutableArray<RestoreDependencyEvidence> Graph, ImmutableHashSet<string> Nodes) ParseAssets(string path)
    {
        try
        {
            using var document = ReadJson(path, "The restore assets file is unreadable or malformed.");
            if (!document.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The restore assets file has no target graph.");
            }

            var targetKeys = targets.EnumerateObject().Select(static target => target.Name).Order(StringComparer.Ordinal).ToImmutableArray();
            var graph = new List<RestoreDependencyEvidence>();
            var nodes = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
            var projectMetadata = ParseProjectDependencyMetadata(document.RootElement);
            AddProjectPackageDependencies(document.RootElement, targets, projectMetadata, graph);
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The restore assets target graph is malformed.");
                }

                foreach (var package in target.Value.EnumerateObject())
                {
                    if (!package.Value.TryGetProperty("type", out var packageType) ||
                        !string.Equals(packageType.GetString(), "project", StringComparison.OrdinalIgnoreCase))
                    {
                        nodes.Add(package.Name);
                    }
                    if (!package.Value.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var dependency in dependencies.EnumerateObject())
                    {
                        var range = DependencyRange(dependency.Value);
                        var resolved = ResolvePackageVersion(target.Value, dependency.Name);
                        if (string.IsNullOrWhiteSpace(range))
                        {
                            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore graph contains a dependency without a version range.");
                        }

                        var metadata = projectMetadata.FirstOrDefault(candidate =>
                            string.Equals(candidate.Framework, target.Name, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(candidate.Id, dependency.Name, StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrEmpty(metadata.Id) && projectMetadata.Count(candidate =>
                                string.Equals(candidate.Id, dependency.Name, StringComparison.OrdinalIgnoreCase)) == 1)
                        {
                            metadata = projectMetadata.First(candidate =>
                                string.Equals(candidate.Id, dependency.Name, StringComparison.OrdinalIgnoreCase));
                        }
                        graph.Add(new RestoreDependencyEvidence(dependency.Name, resolved ?? range, target.Name, metadata.IsPrivate, metadata.IsDevelopmentDependency)
                        {
                            VersionRange = range,
                            ResolvedVersion = resolved,
                            Source = package.Name
                        });
                    }
                }
            }

            return (targetKeys, graph.OrderBy(static edge => edge.TargetFramework, StringComparer.Ordinal).ThenBy(static edge => edge.Id, StringComparer.OrdinalIgnoreCase).ThenBy(static edge => edge.Version, StringComparer.Ordinal).ToImmutableArray(), nodes.ToImmutable());
        }
        catch (NetWasmPackException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The restore assets file is unreadable or malformed.");
        }
    }

    private static void AddProjectPackageDependencies(
        JsonElement root,
        JsonElement targets,
        ImmutableArray<(string Framework, string Id, bool IsPrivate, bool IsDevelopmentDependency)> projectMetadata,
        List<RestoreDependencyEvidence> graph)
    {
        AddProjectReferenceDependencies(root, targets, graph);

        if (!root.TryGetProperty("project", out var project) ||
            !project.TryGetProperty("frameworks", out var frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("dependencies", out var dependencies) ||
                dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!targets.TryGetProperty(framework.Name, out var target) || target.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                var range = DependencyRange(dependency.Value);
                if (string.IsNullOrWhiteSpace(range))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore graph contains a dependency without a version range.");
                }

                if (string.Equals(TargetPackageType(target, dependency.Name), "project", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resolved = ResolvePackageVersion(target, dependency.Name);
                var metadata = projectMetadata.FirstOrDefault(candidate =>
                    string.Equals(candidate.Framework, framework.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Id, dependency.Name, StringComparison.OrdinalIgnoreCase));
                graph.Add(new RestoreDependencyEvidence(
                    dependency.Name,
                    resolved ?? range,
                    framework.Name,
                    metadata.IsPrivate,
                    metadata.IsDevelopmentDependency)
                {
                    VersionRange = range,
                    ResolvedVersion = resolved,
                    Source = "project"
                });
            }
        }
    }

    private static void AddProjectReferenceDependencies(
        JsonElement root,
        JsonElement targets,
        List<RestoreDependencyEvidence> graph)
    {
        if (!root.TryGetProperty("projectFileDependencyGroups", out var groups) ||
            groups.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var group in groups.EnumerateObject())
        {
            if (!targets.TryGetProperty(group.Name, out var target) || target.ValueKind != JsonValueKind.Object ||
                group.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var dependency in group.Value.EnumerateArray())
            {
                if (dependency.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = dependency.GetString()!;
                if (!TryGetProjectReferenceId(value, out var id))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore graph contains a malformed project reference.");
                }

                var packageType = TargetPackageType(target, id);
                if (packageType is null)
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore graph contains an unresolved project reference.");
                }
                if (!string.Equals(packageType, "project", StringComparison.OrdinalIgnoreCase) ||
                    !TryParseProjectReference(value, out _, out var range))
                {
                    if (string.Equals(packageType, "project", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore graph contains a malformed project reference range.");
                    }

                    continue;
                }

                var resolved = ResolvePackageVersion(target, id)!;

                graph.Add(new RestoreDependencyEvidence(id, resolved, group.Name, false, false)
                {
                    VersionRange = range,
                    ResolvedVersion = resolved,
                    Source = "project"
                });
            }
        }
    }

    private static bool TryParseProjectReference(string value, out string id, out string range)
    {
        id = string.Empty;
        range = string.Empty;
        var parts = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        id = parts[0];
        var requested = parts[1].Trim();
        if (requested.StartsWith(">=", StringComparison.Ordinal))
        {
            var minimum = requested[2..].Trim();
            if (NuGetVersion.TryParse(minimum, out _))
            {
                range = $"[{minimum}, )";
            }
        }
        else
        {
            range = requested;
        }

        return range.Length > 0;
    }

    private static bool TryGetProjectReferenceId(string value, out string id)
    {
        id = string.Empty;
        var separator = value.IndexOf(' ');
        if (separator <= 0)
        {
            return false;
        }

        id = value[..separator];
        return id.Length > 0;
    }

    private static string? DependencyRange(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Object when value.TryGetProperty("version", out var version) => version.GetString(),
        _ => null
    };

    private static string? TargetPackageType(JsonElement target, string id)
    {
        foreach (var package in target.EnumerateObject())
        {
            if (package.Name.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase) &&
                package.Value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                return type.GetString();
            }
        }

        return null;
    }

    private static JsonDocument ReadJson(string path, string message)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonDocument.Parse(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, message);
        }
    }

    private static ImmutableArray<(string Framework, string Id, bool IsPrivate, bool IsDevelopmentDependency)> ParseProjectDependencyMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("project", out var project) ||
            !project.TryGetProperty("frameworks", out var frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var metadata = ImmutableArray.CreateBuilder<(string Framework, string Id, bool IsPrivate, bool IsDevelopmentDependency)>();
        foreach (var framework in frameworks.EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                var isPrivate = false;
                var isDevelopment = false;
                if (dependency.Value.ValueKind == JsonValueKind.Object)
                {
                    isPrivate = dependency.Value.TryGetProperty("privateAssets", out var privateAssets) &&
                        string.Equals(privateAssets.GetString(), "all", StringComparison.OrdinalIgnoreCase);
                    isDevelopment = dependency.Value.TryGetProperty("developmentDependency", out var development) &&
                        development.ValueKind == JsonValueKind.True;
                }

                metadata.Add((framework.Name, dependency.Name, isPrivate, isDevelopment));
            }
        }

        return metadata.ToImmutable();
    }

    private static void ValidateLockParity(
        JsonDocument? lockDocument,
        (ImmutableArray<string> TargetKeys, ImmutableArray<RestoreDependencyEvidence> Graph, ImmutableHashSet<string> Nodes) assets,
        ImmutableArray<string> expectedTargetKeys)
    {
        if (lockDocument is null || !lockDocument.RootElement.TryGetProperty("dependencies", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var lockTargetKeys = dependencies.EnumerateObject().Select(static target => target.Name).ToImmutableHashSet(StringComparer.Ordinal);
        if (!lockTargetKeys.SetEquals(expectedTargetKeys))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK005, "The restore lock target keys differ from the restored target keys.");
        }

        var lockNodes = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in dependencies.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The restore lock graph is malformed.");
            }

            foreach (var package in target.Value.EnumerateObject())
            {
                if (!package.Value.TryGetProperty("resolved", out var resolved) || resolved.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(resolved.GetString()))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore lock graph contains an unresolved dependency.");
                }

                var node = $"{package.Name}/{resolved.GetString()}";
                if (!lockNodes.Add(node))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore lock graph contains a duplicate package node.");
                }
                if (!assets.Nodes.Contains(node))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore lock graph contains an edge absent from project.assets.json.");
                }
            }
        }

        if (assets.Nodes.Any(node => !lockNodes.Contains(node)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The project.assets.json graph contains a package absent from the restore lock graph.");
        }
    }

    private static string? ResolvePackageVersion(JsonElement target, string id)
    {
        foreach (var package in target.EnumerateObject())
        {
            if (package.Name.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase))
            {
                return package.Name[(id.Length + 1)..];
            }
        }

        return null;
    }

    private static bool SameGraph(ImmutableArray<RestoreDependencyEvidence> expected, ImmutableArray<RestoreDependencyEvidence> actual)
    {
        static string Key(RestoreDependencyEvidence edge) => $"{edge.Id}|{edge.VersionRange}|{edge.ResolvedVersion}|{edge.TargetFramework}|{edge.Source}";
        return expected.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(actual.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static string HashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "A restore evidence file could not be read.");
        }
    }
}
