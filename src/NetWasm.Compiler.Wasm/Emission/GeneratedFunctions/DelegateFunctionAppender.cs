using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// Each delegate type's invoke function, then each one's multicast function, in the
// order the module plan numbers them.
internal sealed class DelegateFunctionAppender(
    IDelegateInvokeFunctionEmitter invokes,
    IDelegateMulticastFunctionEmitter multicasts,
    IManagedMethodFunctionTypeResolver functionTypes) : IDelegateFunctionAppender
{
    public void Append(IList<WasmFunctionDefinition> functions,
        ImmutableArray<MethodInstanceModel> invokeMethods,
        DelegateInvokeTarget invokeTarget,
        FunctionIndexMap indices,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(invokeTarget);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(functionIndices);

        foreach (var invoke in invokeMethods)
        {
            functions.Add(new(
                $"delegate.invoke<{invoke.DeclaringType.CanonicalName}>",
                functionTypes.Resolve(invoke),
                invokes.Emit(invoke, invokeTarget, functionIndices,
                    indices.GetDelegateMulticastHelper(invoke.DeclaringType.CanonicalName))));
        }
        // A multicast function has one caller, which Binaryen would inline it into, and
        // the invoke function would carry its loop's frame on every single-cast call again.
        foreach (var invoke in invokeMethods)
        {
            functions.Add(new(
                $"delegate.multicast<{invoke.DeclaringType.CanonicalName}>",
                functionTypes.Resolve(invoke),
                multicasts.Emit(invoke, invokeTarget, functionIndices,
                    indices.GetDelegateInvokeHelper(invoke.DeclaringType.CanonicalName)))
            {
                IsNoInlining = true,
            });
        }
    }
}
