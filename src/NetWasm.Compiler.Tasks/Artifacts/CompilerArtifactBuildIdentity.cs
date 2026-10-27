using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Tasks.Artifacts;

// The inputs a semantic build id hashes; the names are those the id has always used.
internal sealed record CompilerArtifactBuildIdentity(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    string Profile,
    string Target,
    string FeatureSet,
    string SdkVersion,
    string CompilerVersion,
    string RuntimeAbiVersion,
    string RuntimeVersion,
    [property: JsonPropertyName("inputs")] ImmutableArray<CompilerArtifactManifestInput> Inputs,
    [property: JsonPropertyName("outputs")] CompilerArtifactBuildIdentityOutput[] Outputs);

internal sealed record CompilerArtifactBuildIdentityOutput(
    string Kind,
    string MediaType,
    string Path,
    string Sha256,
    string CopyToPublishDirectory);
