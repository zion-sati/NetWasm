using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record DelegateInvokeTarget(
    ImmutableArray<ManagedDelegateBinding> Bindings,
    ModuleDataPlan ModuleData);

internal sealed class DelegateInvokeFunctionEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IManagedMethodFunctionTypeResolver functionTypes,
    IDelegateInvocationTargetSelector targets,
    IGeneratedFunctionWriterFactory writers,
    IStaticInitializationEmitter initialization,
    IMethodInitializationTriggerClassifier initializationTriggers) : IDelegateInvokeFunctionEmitter
{
    public byte[] Emit(
        MethodInstanceModel invoke,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices)
    {
        var functionType = functionTypes.Resolve(invoke);
        var parameterCount = functionType.Parameters.Length;
        var resultLocal = parameterCount;
        var rootFrameLocal = resultLocal + 1;
        var invocationListLocal = rootFrameLocal + 1;
        var invocationCountLocal = invocationListLocal + 1;
        var invocationIndexLocal = invocationCountLocal + 1;
        var currentDelegateLocal = invocationIndexLocal + 1;
        var initializationFrameLocal = currentDelegateLocal + 1;
        var methodIdLocal = initializationFrameLocal + 1;
        var returnsValue = invoke.Signature.ReturnSignatureType.StackKind ==
            CliValueKind.ValueType;
        var receiverParameter = returnsValue ? 1 : 0;
        var rootSources = new List<(int Parameter, int Offset)> { (receiverParameter, -1) };
        for (var index = 0; index < invoke.Signature.ParameterSignatureTypes.Length; index++)
        {
            var type = invoke.Signature.ParameterSignatureTypes[index];
            var parameter = receiverParameter + 1 + index;
            if (type.StackKind == CliValueKind.ManagedReference)
            {
                rootSources.Add((parameter, -1));
            }
            else if (type.StackKind == CliValueKind.ValueType)
            {
                foreach (var offset in values.GetValueLayout(type).ReferenceOffsets)
                {
                    rootSources.Add((parameter, offset));
                }
            }
        }

        var output = writers.Create();
        var code = output.Instructions;
        var resultType = !returnsValue && invoke.Signature.ReturnType != CliValueKind.Void
            ? invoke.Signature.ReturnType
            : CliValueKind.I4;
        WasmLocalDeclarationWriter.Write(
            output.Bytes,
            [
                resultType,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                // The method id, read once when targets are searched.
                .. targets.Select(invoke, targetProgram.Bindings).Length > SequentialTargets
                    ? [CliValueKind.I4]
                    : ImmutableArray<CliValueKind>.Empty,
            ],
            layouts.Target);

        EmitDelegateInvokeHelperCore(
            code,
            invoke,
            receiverParameter,
            resultLocal,
            returnsValue,
            invocationListLocal,
            invocationCountLocal,
            invocationIndexLocal,
            currentDelegateLocal,
            rootSources,
            rootFrameLocal,
            initializationFrameLocal,
            methodIdLocal,
            targetProgram,
            functionIndices, functionIndices.ManagedThrowFunction);

        // An exception leaves the root frame in place for its catcher to rewind.
        WriteLocalGet(code, invocationListLocal);
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteLocalGet(code, rootFrameLocal);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        if (!returnsValue && invoke.Signature.ReturnType != CliValueKind.Void)
        {
            WriteLocalGet(code, resultLocal);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return output.Snapshots.Read();
    }

    private void EmitDelegateInvokeHelperCore(
        IWasmInstructionWriter code,
        MethodInstanceModel invoke,
        int receiverParameter,
        int resultLocal,
        bool returnsValue,
        int invocationListLocal,
        int invocationCountLocal,
        int invocationIndexLocal,
        int currentDelegateLocal,
        List<(int Parameter, int Offset)> rootSources,
        int rootFrameLocal,
        int initializationFrameLocal,
        int methodIdLocal,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices,
        OptionalFunctionIndex throwFunction)
    {
        // A single-cast delegate reads nothing once its target runs, so only a
        // multicast one roots itself and the arguments its later entries take.
        WriteLocalGet(code, receiverParameter);
        EmitReferenceLoad(code, objects.DelegateInvocationListOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)invocationListLocal)));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32Constant(code, 1);
        WriteLocalSet(code, invocationCountLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitRootFrame(code, rootSources, rootFrameLocal);
        WriteLocalGet(code, receiverParameter);
        WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateInvocationCountOffset);
        WriteLocalSet(code, invocationCountLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        WriteI32Constant(code, 0);
        WriteLocalSet(code, invocationIndexLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteLocalGet(code, invocationIndexLocal);
        WriteLocalGet(code, invocationCountLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));

        WriteLocalGet(code, invocationListLocal);
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteLocalGet(code, receiverParameter);
        WriteLocalSet(code, currentDelegateLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        ManagedMemoryEmitter.EmitArrayElementAddress(
            code,
            layouts.Target,
            invocationListLocal,
            invocationIndexLocal,
            layouts.Target.ObjectReferenceSize);
        EmitReferenceLoad(code, 0);
        WriteLocalSet(code, currentDelegateLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        EmitLeafInvoke(
            code,
            invoke,
            currentDelegateLocal,
            resultLocal,
            returnsValue,
            rootSources,
            initializationFrameLocal,
            methodIdLocal,
            targetProgram,
            functionIndices, throwFunction);
        WriteLocalGet(code, invocationIndexLocal);
        WriteI32Constant(code, 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        WriteLocalSet(code, invocationIndexLocal);
        WriteBranch(code, 0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // The delegate holds its target's function index. A few targets are tested
    // in turn; more jump through a branch table on the index when the indices
    // are dense, or are found by binary search, so a delegate type bound to
    // hundreds of methods does not test each of them per call.
    private const int SequentialTargets = 4;
    private const int TableSpread = 4;

    private void EmitLeafInvoke(
        IWasmInstructionWriter code,
        MethodInstanceModel invoke,
        int delegateLocal,
        int resultLocal,
        bool returnsValue,
        List<(int Parameter, int Offset)> rootSources,
        int initializationFrameLocal,
        int methodIdLocal,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices,
        OptionalFunctionIndex throwFunction)
    {
        var compatible = targets.Select(invoke, targetProgram.Bindings);
        void Invoke(ManagedDelegateBinding binding) => EmitTargetCall(
            code, invoke, binding.Target, delegateLocal, resultLocal, returnsValue, rootSources,
            initializationFrameLocal, targetProgram, functionIndices);
        if (compatible.Length <= SequentialTargets)
        {
            EmitSequentialTargets(code, compatible, functionIndices, throwFunction, Invoke, () =>
            {
                WriteLocalGet(code, delegateLocal);
                WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateMethodIdOffset);
            });
            return;
        }

        WriteLocalGet(code, delegateLocal);
        WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateMethodIdOffset);
        WriteLocalSet(code, methodIdLocal);
        var ordered = compatible.DistinctBy(binding => functionIndices.Resolve(binding.Target))
            .OrderBy(binding => functionIndices.Resolve(binding.Target)).ToImmutableArray();
        var first = functionIndices.Resolve(ordered[0].Target);
        var span = functionIndices.Resolve(ordered[^1].Target) - first + 1;
        if (span <= ordered.Length * TableSpread)
        {
            EmitTargetTable(code, ordered, first, span, functionIndices, throwFunction, Invoke, methodIdLocal);
            return;
        }
        EmitTargetSearch(code, ordered, functionIndices, throwFunction, Invoke, methodIdLocal);
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
        if (ordered.Length <= SequentialTargets)
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
        IWasmInstructionWriter code,
        MethodInstanceModel invoke,
        MethodInstanceModel target,
        int delegateLocal,
        int resultLocal,
        bool returnsValue,
        List<(int Parameter, int Offset)> rootSources,
        int initializationFrameLocal,
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices)
    {
        // A type initializer may allocate before the call reads the delegate
        // and the arguments, so they stay rooted while it runs.
        if (initializationTriggers.Classify(target.Definition))
        {
            EmitRootFrame(code, rootSources, initializationFrameLocal);
            initialization.Emit(new(target.Definition.DeclaringType,
                target.DeclaringType, targetProgram.ModuleData, IsMethodInvocation: true),
                code, functionIndices);
            WriteLocalGet(code, initializationFrameLocal);
            WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        }
        if (returnsValue)
        {
            WriteLocalGet(code, 0);
        }
        if (!target.Definition.IsStatic)
        {
            WriteLocalGet(code, delegateLocal);
            EmitReferenceLoad(code, objects.DelegateTargetOffset);
            if (target.DeclaringType.IsValueType)
            {
                var value = values.GetValueLayout(target.DeclaringType);
                EmitAddressConstant(code, WasmTargetLayout.Align(
                    layouts.Target.ObjectHeaderSize, value.Alignment));
                EmitAddressAdd(code);
            }
        }
        var firstArgument = returnsValue ? 2 : 1;
        for (var index = 0; index < invoke.Signature.ParameterTypes.Length; index++)
        {
            WriteLocalGet(code, firstArgument + index);
        }
        WriteCall(code, functionIndices.Resolve(target));
        if (!returnsValue && invoke.Signature.ReturnType != CliValueKind.Void)
        {
            WriteLocalSet(code, resultLocal);
        }
    }

    // Publishes the delegate and the references its arguments hold.
    private void EmitRootFrame(
        IWasmInstructionWriter code,
        List<(int Parameter, int Offset)> rootSources,
        int frameLocal)
    {
        WriteI32Constant(code, rootSources.Count);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        WriteLocalSet(code, frameLocal);
        for (var slot = 0; slot < rootSources.Count; slot++)
        {
            (var parameter, var offset) = rootSources[slot];
            WriteLocalGet(code, frameLocal);
            if (slot != 0)
            {
                EmitAddressConstant(code, slot * layouts.Target.ObjectReferenceSize);
                EmitAddressAdd(code);
            }
            WriteLocalGet(code, parameter);
            if (offset >= 0)
            {
                EmitReferenceLoad(
                    code,
                    offset);
            }
            EmitReferenceStore(code, 0);
        }
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
            WriteI32Constant(code, value);
        }
    }

    private void EmitAddressAdd(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64Add
                : WasmOpcodes.I32Add));
    }

    private void EmitReferenceEqualZero(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
    }

    private void EmitReferenceLoad(IWasmInstructionWriter code, int offset)
    {
        var (opcode, alignment) = layouts.Target.UsesMemory64
            ? (WasmOpcodes.I64Load, 3u)
            : (WasmOpcodes.I32Load, 2u);
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(
                alignment,
                checked((uint)offset))));
    }

    private void EmitReferenceStore(IWasmInstructionWriter code, int offset)
    {
        var (opcode, alignment) = layouts.Target.UsesMemory64
            ? (WasmOpcodes.I64Store, 3u)
            : (WasmOpcodes.I32Store, 2u);
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(
                alignment,
                checked((uint)offset))));
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
