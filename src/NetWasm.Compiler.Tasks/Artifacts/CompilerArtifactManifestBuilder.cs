using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerArtifactManifestBuilder : ICompilerArtifactManifestBuilder
{
    private const int SchemaVersion = 1;
    private readonly IArtifactFileDigestCalculator _digests;

    public CompilerArtifactManifestBuilder(IArtifactFileDigestCalculator digests)
    {
        _digests = digests ?? throw new ArgumentNullException(nameof(digests));
    }

    public CompilerArtifactManifestBuildResult Build(CompilerArtifactManifestBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(request);

        var projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        var inputs = request.Inputs
            .Select(input => CreateInput(input, projectDirectory))
            .OrderBy(input => input.Kind, StringComparer.Ordinal)
            .ThenBy(input => input.Path, StringComparer.Ordinal)
            .ThenBy(input => input.Sha256, StringComparer.Ordinal)
            .ToImmutableArray();
        var outputIdentities = request.Outputs
            .Select(output => CreateOutput(output, projectDirectory))
            .OrderBy(output => output.Kind, StringComparer.Ordinal)
            .ThenBy(output => output.Path, StringComparer.Ordinal)
            .ThenBy(output => output.Sha256, StringComparer.Ordinal)
            .ToImmutableArray();

        var semanticBuildId = CreateSemanticBuildId(request, inputs, outputIdentities);
        var artifacts = outputIdentities
            .Select(output => new CompilerArtifactManifestArtifact(
                output.Kind,
                output.Path,
                output.MediaType,
                output.Sha256,
                SchemaVersion,
                request.Target,
                request.Profile,
                semanticBuildId))
            .ToImmutableArray();
        var manifest = new CompilerArtifactManifest(
            SchemaVersion,
            semanticBuildId,
            request.Profile,
            request.Target,
            request.FeatureSet,
            request.SdkVersion,
            request.CompilerVersion,
            request.RuntimeAbiVersion,
            request.RuntimeVersion,
            inputs,
            artifacts);
        var results = outputIdentities
            .Select((output, index) => new CompilerArtifactResult(
                output.FullPath,
                artifacts[index]))
            .ToImmutableArray();

        return new CompilerArtifactManifestBuildResult(manifest, results);
    }

    private CompilerArtifactManifestInput CreateInput(
        CompilerArtifactInputRequest input,
        string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Path);
        var fullPath = Path.GetFullPath(input.Path);
        return new CompilerArtifactManifestInput(
            input.Kind,
            NormalizePath(fullPath, projectDirectory),
            _digests.Calculate(fullPath));
    }

    private CompilerArtifactOutputIdentity CreateOutput(
        CompilerArtifactOutputRequest output,
        string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(output.MediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(output.Path);
        var fullPath = Path.GetFullPath(output.Path);
        return new CompilerArtifactOutputIdentity(
            fullPath,
            output.Kind,
            output.MediaType,
            NormalizePath(fullPath, projectDirectory),
            _digests.Calculate(fullPath));
    }

    private static void ValidateMetadata(CompilerArtifactManifestBuildRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FeatureSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SdkVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CompilerVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeAbiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeVersion);
    }

    private static string NormalizePath(string fullPath, string projectDirectory)
    {
        var relative = Path.GetRelativePath(projectDirectory, fullPath);
        var isOutsideProject = relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        var normalized = isOutsideProject
            ? $"external/{Path.GetFileName(fullPath)}"
            : relative;
        return normalized.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string CreateSemanticBuildId(
        CompilerArtifactManifestBuildRequest request,
        ImmutableArray<CompilerArtifactManifestInput> inputs,
        ImmutableArray<CompilerArtifactOutputIdentity> outputs)
    {
        var identity = JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            request.Profile,
            request.Target,
            request.FeatureSet,
            request.SdkVersion,
            request.CompilerVersion,
            request.RuntimeAbiVersion,
            request.RuntimeVersion,
            inputs,
            outputs = outputs.Select(output => new
            {
                output.Kind,
                output.MediaType,
                output.Path,
                output.Sha256,
            }),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private sealed record CompilerArtifactOutputIdentity(
        string FullPath,
        string Kind,
        string MediaType,
        string Path,
        string Sha256);
}
