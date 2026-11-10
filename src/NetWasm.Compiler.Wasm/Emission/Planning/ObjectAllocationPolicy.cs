namespace NetWasm.Compiler.Wasm.Emission.Planning;

// How code allocates objects of known layout: the module's runtime imports, and
// whether the runtime inlines allocate_object there (O1 to O3).
internal readonly record struct ObjectAllocationPolicy(RuntimeImportSelection Selection, bool Inline);
