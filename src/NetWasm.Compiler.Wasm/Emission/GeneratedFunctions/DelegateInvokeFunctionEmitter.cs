using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record DelegateInvokeTarget(
    FunctionIndexMap FunctionIndices,
    ImmutableArray<ManagedDelegateBinding> Bindings);

internal sealed class DelegateInvokeFunctionEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IExceptionPayloadBlockEmitter exceptionPayloadBlocks,
    IManagedMethodFunctionTypeResolver functionTypes,
    IDelegateInvocationTargetSelector targets,
    IGeneratedFunctionWriterFactory writers) : IDelegateInvokeFunctionEmitter
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
        var exceptionLocal = rootFrameLocal + 1;
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
            [resultType, CliValueKind.ManagedAddress, CliValueKind.ManagedReference],
            layouts.Target);
        WriteI32Constant(code, rootSources.Count);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        WriteLocalSet(code, rootFrameLocal);
        for (var slot = 0; slot < rootSources.Count; slot++)
        {
            (var parameter, var offset) = rootSources[slot];
            WriteLocalGet(code, rootFrameLocal);
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

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptionPayloadBlocks.Emit(code);
        WriteTryTableCatch(code, 0, 0);
        EmitDelegateInvokeHelperCore(
            code,
            invoke,
            receiverParameter,
            resultLocal,
            returnsValue,
            targetProgram,
            functionIndices);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        WriteBranch(code, 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        WriteLocalSet(code, exceptionLocal);
        WriteLocalGet(code, rootFrameLocal);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        WriteLocalGet(code, exceptionLocal);
        WriteThrow(code, 0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        WriteLocalGet(code, rootFrameLocal);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
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
        DelegateInvokeTarget targetProgram,
        IFunctionIndexResolver functionIndices)
    {
        var helperIndex = targetProgram.FunctionIndices.GetDelegateInvokeHelper(
            invoke.DeclaringType.CanonicalName).Value;
        WriteLocalGet(code, receiverParameter);
        EmitReferenceLoad(code, objects.DelegateLeftOffset);
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitDelegateHelperCall(
            code,
            invoke,
            receiverParameter,
            objects.DelegateLeftOffset,
            helperIndex,
            returnsValue);
        if (!returnsValue && invoke.Signature.ReturnType != CliValueKind.Void)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
        }
        EmitDelegateHelperCall(
            code,
            invoke,
            receiverParameter,
            objects.DelegateRightOffset,
            helperIndex,
            returnsValue);
        if (!returnsValue && invoke.Signature.ReturnType != CliValueKind.Void)
        {
            WriteLocalSet(code, resultLocal);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));

        var compatible = targets.Select(invoke, targetProgram.Bindings);
        foreach (var binding in compatible)
        {
            var target = binding.Target;
            WriteLocalGet(code, receiverParameter);
            WriteLoad(code, WasmOpcodes.I32Load, 2, objects.DelegateMethodIdOffset);
            WriteI32Constant(code, functionIndices.Resolve(target));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            if (returnsValue)
            {
                WriteLocalGet(code, 0);
            }
            if (!target.Definition.IsStatic)
            {
                WriteLocalGet(code, receiverParameter);
                EmitReferenceLoad(code, objects.DelegateTargetOffset);
                if (target.DeclaringType.IsValueType)
                {
                    var value = values.GetValueLayout(target.DeclaringType);
                    EmitAddressConstant(code, WasmTargetLayout.Align(
                        layouts.Target.ObjectHeaderSize, value.Alignment));
                    EmitAddressAdd(code);
                }
            }
            var firstArgument = receiverParameter + 1;
            for (var index = 0; index < invoke.Signature.ParameterTypes.Length; index++)
            {
                WriteLocalGet(code, firstArgument + index);
            }
            WriteCall(code, functionIndices.Resolve(target));
            if (!returnsValue && invoke.Signature.ReturnType != CliValueKind.Void)
            {
                WriteLocalSet(code, resultLocal);
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        for (var index = 0; index < compatible.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitDelegateHelperCall(
        IWasmInstructionWriter code,
        MethodInstanceModel invoke,
        int receiverParameter,
        int childOffset,
        int helperIndex,
        bool returnsValue)
    {
        if (returnsValue)
        {
            WriteLocalGet(code, 0);
        }
        WriteLocalGet(code, receiverParameter);
        EmitReferenceLoad(code, childOffset);
        var firstArgument = receiverParameter + 1;
        for (var index = 0; index < invoke.Signature.ParameterTypes.Length; index++)
        {
            WriteLocalGet(code, firstArgument + index);
        }
        WriteCall(code, helperIndex);
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

    private static void WriteThrow(IWasmInstructionWriter code, int tagIndex) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(checked((uint)tagIndex))));

    private static void WriteTryTableCatch(
        IWasmInstructionWriter code,
        int tagIndex,
        int labelDepth) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                checked((uint)tagIndex),
                checked((uint)labelDepth))));
}
