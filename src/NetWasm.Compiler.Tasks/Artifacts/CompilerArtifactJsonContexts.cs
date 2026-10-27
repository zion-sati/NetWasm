using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.Artifacts;

// The semantic build identity is hashed, so it keeps the serializer defaults.
[JsonSerializable(typeof(CompilerArtifactBuildIdentity))]
[ExcludeFromCodeCoverage]
internal sealed partial class CompilerArtifactIdentityJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CompilerArtifactManifest))]
[JsonSerializable(typeof(HostInteropManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class CompilerArtifactReadJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\n")]
[JsonSerializable(typeof(CompilerArtifactManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class CompilerArtifactManifestJsonContext : JsonSerializerContext;
