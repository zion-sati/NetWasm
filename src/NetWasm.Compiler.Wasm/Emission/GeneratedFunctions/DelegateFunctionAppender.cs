using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class DelegateFunctionAppender(
    IDelegateCountFunctionEmitter counts,
    IDelegateLeafFunctionEmitter leaves,
    IDelegateEqualityFunctionEmitter equality,
    IDelegateRemoveFunctionEmitter removal,
    IDelegateInvokeFunctionEmitter invokes,
    IManagedMethodFunctionTypeResolver functionTypes) : IDelegateFunctionAppender
{
    public void Append(IList<WasmFunctionDefinition> functions,
        ImmutableArray<MethodInstanceModel> invokeMethods,
        ImmutableArray<CliTypeIdentity> delegateTypes,
        OptionalFunctionIndex countIndex, OptionalFunctionIndex leafIndex,
        OptionalFunctionIndex equalityIndex, DelegateInvokeTarget invokeTarget,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(invokeTarget);
        ArgumentNullException.ThrowIfNull(functionIndices);

        foreach (var invoke in invokeMethods)
        {
            functions.Add(new(
                $"delegate.invoke<{invoke.DeclaringType.CanonicalName}>",
                functionTypes.Resolve(invoke),
                invokes.Emit(invoke, invokeTarget, functionIndices)));
        }
        if (!equalityIndex.IsPresent)
        {
            return;
        }

        var target = new DelegateHelperTarget(delegateTypes,
            new(countIndex, leafIndex));
        functions.Add(new("delegate.count",
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.ManagedReference),
            counts.Emit(target)));
        functions.Add(new("delegate.leaf",
            WasmFunctionType.Create(CliValueKind.ManagedReference,
                CliValueKind.ManagedReference, CliValueKind.I4), leaves.Emit(target)));
        functions.Add(new("delegate.equals",
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference), equality.Emit(target)));
        functions.Add(new("delegate.remove",
            WasmFunctionType.Create(CliValueKind.ManagedReference,
                CliValueKind.ManagedReference, CliValueKind.ManagedReference),
            removal.Emit(target)));
    }
}
