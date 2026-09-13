using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IValueTypeEqualityFieldPlanner
{
    ImmutableArray<ValueTypeEqualityField> Plan(CliTypeIdentity type);
}
