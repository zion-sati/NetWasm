using System.Collections.Immutable;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed record CompilerBuildMetadata(
    int SchemaVersion,
    string Target,
    ImmutableArray<string> RuntimeFeatures,
    ImmutableArray<WasmFunctionImport> FunctionImports);
