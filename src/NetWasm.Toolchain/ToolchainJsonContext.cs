using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain;

// The toolchain's JSON contracts. Each reader passes its own options.
[JsonSerializable(typeof(PinnedToolchainManifest))]
[JsonSerializable(typeof(JcoClosureIntegrityManifest))]
[ExcludeFromCodeCoverage]
internal sealed partial class ToolchainJsonContext : JsonSerializerContext;
