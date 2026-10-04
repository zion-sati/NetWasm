using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.StackTraces;

internal interface IStackTraceSourceLocationReader
{
    ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>> Read(
        MetadataCompilationSnapshot metadata,
        CompilerOptions options);
}
