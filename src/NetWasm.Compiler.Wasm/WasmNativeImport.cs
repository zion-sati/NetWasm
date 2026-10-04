using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm;

/// <summary>A reached static native entry with its target-resolved physical signature.</summary>
public sealed record WasmNativeImport(
    string LibraryName,
    string EntryPoint,
    ImmutableArray<WasmValueType> Parameters,
    WasmValueType? ReturnType);
