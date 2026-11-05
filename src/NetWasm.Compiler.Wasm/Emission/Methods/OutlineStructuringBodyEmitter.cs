using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

/// <summary>
/// Structures a deferred method before its body is emitted: an outline stood in for it
/// through planning, and structuring is per method and pure, so the method structures as
/// it would have before planning.
/// </summary>
internal sealed class OutlineStructuringBodyEmitter(
    IManagedMethodBodyEmitter inner,
    IWasmMethodLowerer? structuring) : IManagedMethodBodyEmitter
{
    private readonly IManagedMethodBodyEmitter _inner = inner ??
        throw new ArgumentNullException(nameof(inner));

    public ManagedMethodBodyEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        MethodInstanceModel? methodInstance = null)
    {
        ArgumentNullException.ThrowIfNull(structured);
        if (structured.Outline is { } outline)
        {
            structured = (structuring ?? throw new InvalidOperationException(
                    "A deferred method reached emission without the compile's structuring."))
                .Lower(outline.Body, outline.BranchPrefixDuplication, outline.DispatcherElimination);
        }
        return _inner.Emit(method, callerIdentity, structured, rootMap, target, functionIndices, methodInstance);
    }
}
