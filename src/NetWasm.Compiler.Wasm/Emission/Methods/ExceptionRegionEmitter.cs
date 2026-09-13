using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ExceptionRegionEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IExceptionPayloadBlockEmitter exceptions,
    IMethodFrameExitEmitter methodFrameExits,
    IStructuredExceptionGroupKeyFactory exceptionGroupKeys) : IExceptionRegionEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionRegion region,
        MethodEmissionContext context,
        bool isOutermost,
        ModuleDataPlan moduleData,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? dispatcherContinueDepth = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(moduleData);
        ArgumentNullException.ThrowIfNull(emitSequence);

        if (!method.ExceptionGroups.TryGetValue(region.Group, out var group))
        {
            throw new InvalidOperationException(
                $"structured exception group '{region.Group.Value}' is not defined");
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[group.Id])));
        if (group.Clauses.All(clause => clause.Kind is
                CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter))
        {
            EmitCatchGroup(
                code,
                method,
                group,
                context,
                isOutermost,
                moduleData,
                emitSequence,
                region,
                loopBreakDepth,
                loopContinueDepth,
                dispatcherContinueDepth);
            return;
        }
        if (group.Clauses.Length == 1)
        {
            EmitCleanupGroup(
                code,
                method,
                group,
                context,
                isOutermost,
                moduleData,
                emitSequence,
                region,
                loopBreakDepth,
                loopContinueDepth,
                dispatcherContinueDepth);
            return;
        }
        throw UnsupportedExceptionShape(
            method,
            "an exception group must contain typed catches or one finally clause");
    }

    private void EmitProtectedParts(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        ModuleDataPlan moduleData,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var part in group.ProtectedParts)
        {
            switch (part)
            {
                case StructuredExceptionCode body:
                    emitSequence(
                        body.Body,
                        context with { ActiveExceptionGroup = group },
                        null,
                        null,
                        0);
                    break;
                case StructuredNestedExceptionGroup nested:
                    Emit(
                        code,
                        method,
                        new StructuredExceptionRegion(nested.Group, null),
                        context with { ActiveExceptionGroup = group },
                        false,
                        moduleData,
                        emitSequence);
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[group.Id])));
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.BranchIf,
                        WasmInstructionOperand.Unsigned(0)));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown exception part {part.GetType().Name}.");
            }
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitCatchGroup(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        bool isOutermost,
        ModuleDataPlan moduleData,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth)
    {
        EnterExceptionFrame(code, method, group, context, moduleData);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        EmitProtectedParts(code, method, group, context, moduleData, emitSequence);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        LeaveExceptionFrame(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        LeaveExceptionFrame(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionFrameLocals[group.Id])));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ExceptionFrameTargetClause))));
        var clauseSelector = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            CliValueKind.I4,
            layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)clauseSelector)));
        for (var index = 0; index < group.Clauses.Length; index++)
        {
            var clause = group.Clauses[index];
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)clauseSelector)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(index + 1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                    context.StackLocals,
                    0,
                    CliValueKind.ManagedReference,
                    layouts.Target))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.EndCatch))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Block,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            emitSequence(
                clause.HandlerBody,
                context with
                {
                    LeaveFrameOnRethrow =
                        isOutermost && context.LeaveFrameOnExceptionalExit,
                    ActiveExceptionGroup = group,
                },
                null,
                null,
                0);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        if (isOutermost && context.LeaveFrameOnExceptionalExit)
        {
            methodFrameExits.Emit(code, context);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitNormalContinuations(
            code,
            group,
            context,
            emitSequence,
            region,
            loopBreakDepth,
            loopContinueDepth,
            dispatcherContinueDepth);
    }

    private void EmitCleanupGroup(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        bool isOutermost,
        ModuleDataPlan moduleData,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth)
    {
        var clause = group.Clauses[0];
        if (clause.Kind == CilExceptionRegionKind.Finally)
        {
            EmitFinallyGroup(
                code,
                method,
                group,
                context,
                isOutermost,
                moduleData,
                emitSequence,
                region,
                loopBreakDepth,
                loopContinueDepth,
                dispatcherContinueDepth);
            return;
        }
        EnterExceptionFrame(code, method, group, context, moduleData);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        EmitProtectedParts(code, method, group, context, moduleData, emitSequence);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        LeaveExceptionFrame(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        LeaveExceptionFrame(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        emitSequence(
            clause.HandlerBody,
            context with { ActiveExceptionGroup = group },
            null,
            null,
            0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        if (isOutermost && context.LeaveFrameOnExceptionalExit)
        {
            methodFrameExits.Emit(code, context);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitNormalContinuations(
            code,
            group,
            context,
            emitSequence,
            region,
            loopBreakDepth,
            loopContinueDepth,
            dispatcherContinueDepth);
    }

    private void EmitFinallyGroup(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        bool isOutermost,
        ModuleDataPlan moduleData,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth)
    {
        var clause = group.Clauses[0];
        EmitAddressConstant(code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        EnterExceptionFrame(code, method, group, context, moduleData);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        EmitProtectedParts(code, method, group, context, moduleData, emitSequence);
        EmitAddressConstant(code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        LeaveExceptionFrame(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        LeaveExceptionFrame(code, group, context);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        emitSequence(
            clause.HandlerBody,
            context with { ActiveExceptionGroup = group },
            null,
            null,
            0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (isOutermost && context.LeaveFrameOnExceptionalExit)
        {
            methodFrameExits.Emit(code, context);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        EmitNormalContinuations(
            code,
            group,
            context,
            emitSequence,
            region,
            loopBreakDepth,
            loopContinueDepth,
            dispatcherContinueDepth);
    }

    private static void EmitNormalContinuations(
        IWasmInstructionWriter code,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth)
    {
        var selector = context.ExceptionContinuationLocals[group.Id];
        if (group.ContinuationDispatcher is { } dispatcher)
        {
            var defaultIndex = region.FallthroughContinuation?.Value ?? 0;
            SetDispatcherTarget(group.NormalContinuations[defaultIndex]);
            for (var index = 0; index < group.NormalContinuations.Length; index++)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)selector)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(index + 1)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.If,
                    WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                SetDispatcherTarget(group.NormalContinuations[index]);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
            return;

            void SetDispatcherTarget(StructuredExceptionContinuation continuation)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(continuation.Target.Value)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)context.DispatcherProgramCounter)));
            }
        }
        for (var index = 0; index < group.NormalContinuations.Length; index++)
        {
            if (index == region.FallthroughContinuation?.Value)
            {
                continue;
            }
            var continuationId = new StructuredContinuationId(index);
            EmitContinuationSelected(
                code,
                selector,
                index,
                region.DispatcherContinuations.Contains(continuationId) &&
                index == 0);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            var continuation = group.NormalContinuations[index];
            if (region.DispatcherContinuations.Contains(continuationId))
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(continuation.Target.Value)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned(
                        (uint)context.DispatcherProgramCounter)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Branch,
                    WasmInstructionOperand.Unsigned((uint)(
                        (dispatcherContinueDepth ?? throw new InvalidOperationException(
                            "an exception continuation routed through a dispatcher " +
                            "was emitted outside that dispatcher")) + 1))));
            }
            else if (context.ActiveExceptionGroup is { } parent &&
                TryGetContinuationIndex(parent, continuation.Target, out var parentIndex))
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(parentIndex + 1)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[parent.Id])));
            }
            else
            {
                emitSequence(
                    continuation.Body,
                    context,
                    loopBreakDepth is int breakDepth ? breakDepth + 1 : null,
                    loopContinueDepth is int continueDepth ? continueDepth + 1 : null,
                    null);
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void EmitContinuationSelected(
        IWasmInstructionWriter code,
        int selector,
        int continuationIndex,
        bool includeDefault)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)selector)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(continuationIndex + 1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        if (!includeDefault)
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)selector)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
    }

    private static bool TryGetContinuationIndex(
        StructuredExceptionGroup group,
        StructuredBlockId target,
        out int index)
    {
        for (index = 0; index < group.NormalContinuations.Length; index++)
        {
            if (group.NormalContinuations[index].Target == target)
            {
                return true;
            }
        }
        index = -1;
        return false;
    }

    private void EnterExceptionFrame(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        ModuleDataPlan moduleData)
    {
        var metadata = moduleData.ExceptionMetadata[exceptionGroupKeys.Create(method, group.Id)];
        EmitAddressConstant(code, metadata.Address);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(metadata.ClauseCount)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ExceptionFrameEnter))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionFrameLocals[group.Id])));
        if (metadata.HasFilters)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.ExceptionFrameLocals[group.Id])));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ExceptionFrameSetEnvironment))));
        }
    }

    private void LeaveExceptionFrame(
        IWasmInstructionWriter code,
        StructuredExceptionGroup group,
        MethodEmissionContext context)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionFrameLocals[group.Id])));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ExceptionFrameLeave))));
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(value)));
        }
    }

    private static CompilerException UnsupportedExceptionShape(
        StructuredMethod method,
        string message) => new(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedCil,
            message,
            method.Header.Method.Name));
}
