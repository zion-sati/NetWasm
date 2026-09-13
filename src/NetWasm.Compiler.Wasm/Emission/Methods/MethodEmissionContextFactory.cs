using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal static class MethodEmissionContextFactory
{
    public static MethodEmissionLayout Create(
        StructuredMethodHeader header,
        MethodRootMap rootMap,
        int parameterCount,
        int parameterOffset,
        IReadOnlyList<StructuredExceptionGroupId> exceptionGroups,
        ValueFrameLayout valueLayout,
        FilterEnvironmentLayout filterEnvironment,
        ManagedMethodIdentity callerIdentity)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterOffset);
        ArgumentNullException.ThrowIfNull(exceptionGroups);
        ArgumentNullException.ThrowIfNull(valueLayout);
        ArgumentNullException.ThrowIfNull(filterEnvironment);

        var localBase = parameterCount;
        var stackBase = checked(localBase + header.Locals.Length);
        var stackLocals = WasmLocalLayoutPlanner.CreateEvaluationStack(
            stackBase,
            header.MaxStack);
        var objectTemporary = stackLocals.End;
        var rootFrame = checked(objectTemporary + 1);
        var exceptionTemporary = checked(rootFrame + 1);
        var exceptionFrameLocals = new Dictionary<StructuredExceptionGroupId, int>();
        var exceptionContinuationLocals = new Dictionary<StructuredExceptionGroupId, int>();
        for (var index = 0; index < exceptionGroups.Count; index++)
        {
            exceptionFrameLocals.Add(
                exceptionGroups[index],
                checked(exceptionTemporary + 1 + index));
            exceptionContinuationLocals.Add(
                exceptionGroups[index],
                checked(exceptionTemporary + 1 + exceptionGroups.Count + index));
        }

        var valueFrame = checked(exceptionTemporary + 1 + exceptionGroups.Count * 2);
        var filterRootFrame = checked(valueFrame + 1);
        var interopDescriptor = checked(filterRootFrame + 1);
        var interopResult = checked(interopDescriptor + 1);
        var interopHandle = checked(interopResult + 1);
        var numericTemporaryI4 = checked(interopHandle + 1);
        var numericTemporaryI8 = checked(numericTemporaryI4 + 1);
        var dispatcherProgramCounter = checked(numericTemporaryI8 + 1);
        var localTypes = WasmLocalLayoutPlanner.CreateMethodLocals(
            header,
            exceptionGroups.Count,
            valueLayout.SpilledScalarLocals);
        var context = new MethodEmissionContext(
            rootMap,
            localBase,
            stackBase,
            stackLocals,
            objectTemporary,
            rootFrame,
            exceptionTemporary,
            exceptionFrameLocals,
            exceptionContinuationLocals,
            valueFrame,
            valueLayout,
            filterRootFrame,
            filterEnvironment,
            parameterOffset,
            interopDescriptor,
            interopResult,
            interopHandle,
            numericTemporaryI4,
            numericTemporaryI8,
            dispatcherProgramCounter,
            CallerIdentity: callerIdentity);
        return new MethodEmissionLayout(context, localTypes);
    }
}
