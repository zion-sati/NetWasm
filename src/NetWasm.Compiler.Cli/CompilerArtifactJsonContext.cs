using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Cli;

// The compiler's sidecars write every enum they hold by its camel-case name.
[JsonSerializable(typeof(CompilerRuntimeLayout))]
[JsonSerializable(typeof(HostInteropManifest))]
[JsonSerializable(typeof(CompilerMetadataSidecar))]
[ExcludeFromCodeCoverage]
internal sealed partial class CompilerArtifactJsonContext : JsonSerializerContext
{
    public static CompilerArtifactJsonContext Sidecars { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Converters =
        {
            new JsonStringEnumConverter<CliValueKind>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<ManagedExecutableParameterShape>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<ManagedExecutableReturnShape>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<ManagedExecutableCompletionShape>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<WasmValueType>(JsonNamingPolicy.CamelCase),
        },
    });
}
