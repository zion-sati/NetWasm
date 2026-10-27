using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace NetWasm.Testing.VSTest.Hosting;

[JsonSerializable(typeof(PortableTestHostManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class PortableTestHostJsonContext : JsonSerializerContext;
