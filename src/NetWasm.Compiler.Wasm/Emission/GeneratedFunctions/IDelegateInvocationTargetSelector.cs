using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IDelegateInvocationTargetSelector
{
    ImmutableArray<ManagedDelegateBinding> Select(
        MethodInstanceModel invoke,
        ImmutableArray<ManagedDelegateBinding> bindings);
}
