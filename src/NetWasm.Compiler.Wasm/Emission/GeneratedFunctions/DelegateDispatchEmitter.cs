using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// The delegate holds its target's function index. A few targets are tested in turn;
// more jump through a branch table on the index when the indices are dense, or are
// found by binary search, so a delegate type bound to hundreds of methods does not
// test each of them per call.
internal sealed class DelegateDispatchEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IDelegateInvocationTargetSelector targets,
    IStaticInitializationEmitter initialization,
    IMethodInitializationTriggerClassifier initializationTriggers,
    IDelegateArgumentRootEmitter roots) : IDelegateDispatchEmitter
{
    private const int TableSpread = 4;

    public void Emit(IWasmInstructionWriter code, DelegateDispatch dispatch, IFunctionIndexResolver functionIndices)
    {
        var throwFunction = functionIndices.ManagedThrowFunction;
        var compatible = targets.Select(dispatch.Invoke, dispatch.Program.Bindings);
        void Invoke(ManagedDelegateBinding binding) => EmitTargetCall(code, dispatch, functionIndices, binding);
        if (compatible.Length <= DelegateDispatch.SequentialTargets)
        {
            EmitSequentialTargets(code, compatible, functionIndices, throwFunction, Invoke, () =>
            {
                WriteLocalGet(code, dispatch.DelegateLocal);
                WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateMethodIdOffset);
            });
            return;
        }

        WriteLocalGet(code, dispatch.DelegateLocal);
        WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateMethodIdOffset);
        WriteLocalSet(code, dispatch.MethodIdLocal);
        var ordered = compatible.DistinctBy(binding => functionIndices.Resolve(binding.Target))
            .OrderBy(binding => functionIndices.Resolve(binding.Target)).ToImmutableArray();
        var first = functionIndices.Resolve(ordered[0].Target);
        var span = functionIndices.Resolve(ordered[^1].Target) - first + 1;
        if (span <= ordered.Length * TableSpread)
        {
            EmitTargetTable(code, ordered, first, span, functionIndices, throwFunction, Invoke,
                dispatch.MethodIdLocal);
            return;
        }
        EmitTargetSearch(code, ordered, functionIndices, throwFunction, Invoke, dispatch.MethodIdLocal);
    }

    private void EmitTargetTable(
        IWasmInstructionWriter code,
        ImmutableArray<ManagedDelegateBinding> ordered,
        int first,
        int span,
        IFunctionIndexResolver functionIndices,
        OptionalFunctionIndex throwFunction,
        Action<ManagedDelegateBinding> invoke,
        int methodIdLocal)
    {
        // One block to leave once a target returns, one for an unknown index,
        // then one per target; the table names a target's block by its depth.
        var cases = ordered.Select((binding, index) => (Offset: functionIndices.Resolve(binding.Target) - first, Index: index))
            .ToDictionary(entry => entry.Offset, entry => (uint)entry.Index);
        for (var index = 0; index < ordered.Length + 2; index++)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Block,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        }
        WriteLocalGet(code, methodIdLocal);
        WriteI32Constant(code, first);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchTable,
            WasmInstructionOperand.BranchTable(
                [.. Enumerable.Range(0, span).Select(offset =>
                    cases.TryGetValue(offset, out var index) ? index : (uint)ordered.Length)],
                (uint)ordered.Length)));
        for (var index = 0; index < ordered.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            invoke(ordered[index]);
            WriteBranch(code, ordered.Length - index);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitTargetSearch(
        IWasmInstructionWriter code,
        ImmutableArray<ManagedDelegateBinding> ordered,
        IFunctionIndexResolver functionIndices,
        OptionalFunctionIndex throwFunction,
        Action<ManagedDelegateBinding> invoke,
        int methodIdLocal)
    {
        if (ordered.Length <= DelegateDispatch.SequentialTargets)
        {
            EmitSequentialTargets(code, ordered, functionIndices, throwFunction, invoke,
                () => WriteLocalGet(code, methodIdLocal));
            return;
        }
        var middle = ordered.Length / 2;
        WriteLocalGet(code, methodIdLocal);
        WriteI32Constant(code, functionIndices.Resolve(ordered[middle].Target));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitTargetSearch(code, ordered[..middle], functionIndices, throwFunction, invoke, methodIdLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitTargetSearch(code, ordered[middle..], functionIndices, throwFunction, invoke, methodIdLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitSequentialTargets(
        IWasmInstructionWriter code,
        ImmutableArray<ManagedDelegateBinding> bindings,
        IFunctionIndexResolver functionIndices,
        OptionalFunctionIndex throwFunction,
        Action<ManagedDelegateBinding> invoke,
        Action loadMethodId)
    {
        foreach (var binding in bindings)
        {
            loadMethodId();
            WriteI32Constant(code, functionIndices.Resolve(binding.Target));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            invoke(binding);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast, throwFunction);
        for (var index = 0; index < bindings.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void EmitTargetCall(
        IWasmInstructionWriter code, DelegateDispatch dispatch, IFunctionIndexResolver functionIndices,
        ManagedDelegateBinding binding)
    {
        var target = binding.Target;
        var signature = DelegateInvokeSignature.Of(dispatch.Invoke);
        // A type initializer may allocate before the call reads the delegate
        // and the arguments, so they stay rooted while it runs.
        if (initializationTriggers.Classify(target.Definition))
        {
            roots.Emit(code, dispatch.Invoke, dispatch.InitializationFrameLocal);
            initialization.Emit(new(target.Definition.DeclaringType,
                target.DeclaringType, dispatch.Program.StaticInitializerGuards, IsMethodInvocation: true),
                code, functionIndices);
            WriteLocalGet(code, dispatch.InitializationFrameLocal);
            WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        }
        if (signature.ReturnsValue)
        {
            WriteLocalGet(code, 0);
        }
        if (binding.PassesTarget)
        {
            WriteLocalGet(code, dispatch.DelegateLocal);
            WriteLoad(code, layouts.Target.UsesMemory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
                layouts.Target.UsesMemory64 ? 3u : 2u, objects.DelegateTargetOffset);
            if (!target.Definition.IsStatic && target.DeclaringType.IsValueType)
            {
                var value = values.GetValueLayout(target.DeclaringType);
                var payload = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, value.Alignment);
                code.Write(layouts.Target.UsesMemory64
                    ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(payload))
                    : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(payload)));
                code.Write(WasmInstruction.NoOperand(
                    layouts.Target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
            }
        }
        for (var index = 0; index < dispatch.Invoke.Signature.ParameterTypes.Length; index++)
        {
            WriteLocalGet(code, signature.FirstArgument + index);
        }
        WriteCall(code, functionIndices.Resolve(target));
        if (signature.ReturnsScalar)
        {
            WriteLocalSet(code, dispatch.ResultLocal);
        }
    }

    private static void WriteLoad(
        IWasmInstructionWriter code,
        byte opcode,
        uint alignment,
        int offset) =>
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(alignment, checked((uint)offset))));

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteCall(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteBranch(IWasmInstructionWriter code, int depth) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(checked((uint)depth))));
}
