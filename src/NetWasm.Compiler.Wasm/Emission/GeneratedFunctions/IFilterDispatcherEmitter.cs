using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterDispatcherEmitter
{
    byte[] Emit(
        ImmutableArray<FilterFunclet> filters,
        IReadOnlyDictionary<int, int> functionIndices);
}
