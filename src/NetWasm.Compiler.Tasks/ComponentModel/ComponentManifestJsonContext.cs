using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\n")]
[JsonSerializable(typeof(ComponentManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class ComponentManifestJsonContext : JsonSerializerContext;
