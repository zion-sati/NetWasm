using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumStorageResolver
{
    ImmutableArray<EnumStorage> Resolve();
}
