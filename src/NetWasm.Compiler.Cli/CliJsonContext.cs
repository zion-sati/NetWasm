using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\n")]
[JsonSerializable(typeof(ComponentManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class CliJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HostInteropManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class CliReadJsonContext : JsonSerializerContext;
