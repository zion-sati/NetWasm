using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build;

// The JSON the hosting build writes into JavaScript and reads from MSBuild
// items and tools. ComponentTranspiler passes its own options.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(bool[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ComponentTranspiler.JcoTranspileMetadata))]
[ExcludeFromCodeCoverage]
internal sealed partial class HostingBuildJsonContext : JsonSerializerContext;
