using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NetWasm.Sdk.Pack.Archives;
using NetWasm.Sdk.Pack.Diagnostics;
using NetWasm.Sdk.Pack.Packing;
using NetWasm.Sdk.Pack.Provenance;

namespace NetWasm.Sdk.Pack;

// The pack task's JSON. Writers that need other options pass their own.
[JsonSerializable(typeof(PackDiagnosticFile))]
[JsonSerializable(typeof(CanonicalPackManifest))]
[JsonSerializable(typeof(PackRequestFingerprint))]
[JsonSerializable(typeof(SdkProvenanceManifestBuilder.ManifestDocument))]
[JsonSerializable(typeof(JsonObject))]
[ExcludeFromCodeCoverage]
internal sealed partial class SdkPackJsonContext : JsonSerializerContext;
