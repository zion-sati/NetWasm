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
        if (!AreEqual(expected.Manifest, actual))
        {
            throw new InvalidOperationException("NetWasm artifact manifest is stale.");
        }

        return expected;
    }

    private static bool AreEqual(CompilerArtifactManifest expected, CompilerArtifactManifest actual)
    {
        return expected.SchemaVersion == actual.SchemaVersion
            && expected.SemanticBuildId == actual.SemanticBuildId
            && expected.Profile == actual.Profile
            && expected.Target == actual.Target
            && expected.FeatureSet == actual.FeatureSet
            && expected.SdkVersion == actual.SdkVersion
            && expected.CompilerVersion == actual.CompilerVersion
            && expected.RuntimeAbiVersion == actual.RuntimeAbiVersion
            && expected.RuntimeVersion == actual.RuntimeVersion
            && expected.Inputs.SequenceEqual(actual.Inputs)
            && expected.Artifacts.SequenceEqual(actual.Artifacts);
    }
}
