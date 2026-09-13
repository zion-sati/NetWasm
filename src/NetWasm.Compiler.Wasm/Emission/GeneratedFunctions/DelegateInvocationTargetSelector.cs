using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateInvocationTargetSelector :
    IDelegateInvocationTargetSelector
{
    public ImmutableArray<ManagedDelegateBinding> Select(
        MethodInstanceModel invoke,
        ImmutableArray<ManagedDelegateBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        bindings = bindings.IsDefault ? [] : bindings;

        return
        [
            .. bindings
                .Where(binding =>
                    binding.InvokeIdentity.CanonicalName == invoke.CanonicalName)
                .OrderBy(binding =>
                    binding.TargetIdentity.CanonicalName,
                    StringComparer.Ordinal),
        ];
    }
}
