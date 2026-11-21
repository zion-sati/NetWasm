// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Names the method an emission failure came from. Emission reports what a method cannot do as a diagnostic; any other
// exception is a compiler defect, reported against the method instead of as a bare message. A unit replay miss is
// neither: the replay catches it to compile the method afresh, so it passes through as it is.
internal sealed class MethodAttributingManagedMethodBodyEmitter(
    IManagedMethodBodyEmitter inner,
    IMethodDefectExceptionFactory defects) : IManagedMethodBodyEmitter
{
    private readonly IManagedMethodBodyEmitter _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IMethodDefectExceptionFactory _defects = defects ?? throw new ArgumentNullException(nameof(defects));

    public ManagedMethodBodyEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        MethodInstanceModel? methodInstance = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        try
        {
            return _inner.Emit(method, callerIdentity, structured, rootMap, target, functionIndices, methodInstance);
        }
        catch (Exception exception) when (exception is not
            (CompilerException or OperationCanceledException or UnitReplayMissException))
        {
            throw _defects.Create(method, methodInstance?.DeclaringType, "emitting", exception);
        }
    }
}
