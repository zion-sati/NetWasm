using System.Collections.Immutable;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerArtifactManifestValidator : ICompilerArtifactManifestValidator
{
    private readonly ICompilerArtifactManifestBuilder _builder;
    private readonly ICompilerArtifactManifestReader _reader;

    public CompilerArtifactManifestValidator(
        ICompilerArtifactManifestBuilder builder,
        ICompilerArtifactManifestReader reader)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public CompilerArtifactManifestBuildResult Validate(CompilerArtifactManifestValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expected = _builder.Build(request.BuildRequest);
        var actual = _reader.Read(request.ManifestPath);
        if (StaleReason(expected.Manifest, actual) is { } reason)
        {
            throw new StaleCompilerArtifactManifestException(reason);
        }

        return expected;
    }

    // The first way the recorded manifest differs from the one the current build would write.
    private static string? StaleReason(CompilerArtifactManifest expected, CompilerArtifactManifest actual)
    {
        if (expected.SchemaVersion != actual.SchemaVersion) return "it was written in another manifest format";
        if (expected.Profile != actual.Profile) return $"it was built for {actual.Profile}";
        if (expected.Target != actual.Target) return $"it was built for {actual.Target}";
        if (expected.FeatureSet != actual.FeatureSet) return $"it was built with feature set {actual.FeatureSet}";
        if (expected.SdkVersion != actual.SdkVersion) return $"it was built by NetWasm SDK {actual.SdkVersion}";
        if (expected.CompilerVersion != actual.CompilerVersion) return $"it was built by NetWasm compiler {actual.CompilerVersion}";
        if (expected.RuntimeAbiVersion != actual.RuntimeAbiVersion || expected.RuntimeVersion != actual.RuntimeVersion)
        {
            return $"it was built for NetWasm runtime {actual.RuntimeVersion}";
        }

        foreach (var input in expected.Inputs)
        {
            var recorded = actual.Inputs.FirstOrDefault(entry => entry.Kind == input.Kind && entry.Path == input.Path);
            if (recorded is null) return $"{Describe(input)} was not an input of the build";
            if (recorded.Sha256 != input.Sha256) return $"{Describe(input)} changed since the build";
        }

        if (actual.Inputs.FirstOrDefault(recorded => !expected.Inputs.Any(input =>
                input.Kind == recorded.Kind && input.Path == recorded.Path)) is { } removed)
        {
            return $"{Describe(removed)} is no longer an input";
        }

        foreach (var artifact in expected.Artifacts)
        {
            var recorded = actual.Artifacts.FirstOrDefault(entry => entry.Kind == artifact.Kind && entry.Path == artifact.Path);
            if (recorded is null) return $"{artifact.Path} was not an artifact of the build";
            if (recorded != artifact) return $"{artifact.Path} changed since the build";
        }

        if (actual.Artifacts.FirstOrDefault(recorded => !expected.Artifacts.Any(artifact =>
                artifact.Kind == recorded.Kind && artifact.Path == recorded.Path)) is { } dropped)
        {
            return $"{dropped.Path} is no longer an artifact";
        }

        return expected.SemanticBuildId == actual.SemanticBuildId ? null : "its semantic build id differs";
    }

    private static string Describe(CompilerArtifactManifestInput input) =>
        input.Kind == "CompilationIdentity" ? "the compilation settings" : input.Path;
}
