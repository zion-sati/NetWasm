using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

internal interface IStartupPreflightCallEmitter
{
    void Emit(
        GeneratedFunctionWriterLease code,
        ImmutableArray<EntityKey> preflights,
        IFunctionIndexResolver functionIndices);
}
