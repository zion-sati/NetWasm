# NetWasm.System.Text.Json

`NetWasm.System.Text.Json` supplies the `System.Text.Json` assembly for the
`netwasm0.1` profile. It is an independently versioned ported-library package
for applications that opt into the NetWasm JSON and source-generated
serialization surface.

The package includes the upstream-compatible mutable JSON DOM closure:
`JsonNode`, `JsonObject`, `JsonArray`, and `JsonValue`, including synchronous
parsing, mutation, parent/ownership tracking, deep cloning/equality, and
`JsonSerializer` node conversion. DOM serialization/deserialization uses the
same generated `JsonTypeInfo`/`JsonSerializerContext` contract as the rest of
the NetWasm port. Reflection-based metadata discovery, dynamic-code fallback,
schema export, and asynchronous node parsing remain outside the
reflection-free `netwasm0.1` support boundary.

The package carries the `NetWasm,Version=v0.1` asset and preserves the public
`System.Text.Json` assembly identity. Its Roslyn source generator uses
`[JsonSerializable]` declarations to produce reflection-free metadata. It directly
supplies the exact `NetWasm.System.Memory`,
`NetWasm.System.Text.Encodings.Web`, and `NetWasm.System.IO.Pipelines` support
packages required by this port. The package targets only `netwasm0.1`.
