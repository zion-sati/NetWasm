using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NetWasm.Wit.Bindings.Workers;

// The JSON the worker binding writers emit: C# string literals and type kinds.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonNode))]
[ExcludeFromCodeCoverage]
internal sealed partial class WitWorkerJsonContext : JsonSerializerContext;
