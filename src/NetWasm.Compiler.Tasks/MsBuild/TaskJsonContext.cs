using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.MsBuild;

// JSON the tasks hand to MSBuild items and build steps, with the relaxed
// encoder so names stay readable.
[JsonSerializable(typeof(ImmutableArray<string>))]
[JsonSerializable(typeof(ImmutableArray<bool>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(RawBindingManifest))]
[JsonSerializable(typeof(WitWorkerContract))]
[ExcludeFromCodeCoverage]
internal sealed partial class TaskJsonContext : JsonSerializerContext
{
    public static TaskJsonContext Metadata { get; } = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static TaskJsonContext CamelCaseMetadata { get; } = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });
}
