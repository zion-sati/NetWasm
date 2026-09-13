using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ValueTypeEqualsEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    ITypeDescriptorSource descriptors,
    ITypeRepository types,
    IMethodRepository methods,
    IReferenceComparisonEmitter references,
    IValueTypeEqualityFieldPlanner fields) : IValueTypeEqualsEmitter
{
    private readonly Lazy<EntityKey> _objectEquals = new(
        () => ResolveObjectEquals(descriptors, types, methods));

    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        CliValueKind receiverKind)
    {
        var functionIndices = request.FunctionIndices;
        var left = request.Local(0, receiverKind);
        var right = request.Local(1, CliValueKind.ManagedReference);
        var output = request.Local(0, CliValueKind.I4);
        var result = request.Instruction.Context.NumericTemporaryI4;
        var value = values.GetValueLayout(type);
        var boxed = typeLayouts.GetObjectLayout(type);
        var payloadOffset = WasmTargetLayout.Align(
            layouts.Target.ObjectHeaderSize,
            value.Alignment);

        WriteI32(code, 0);
        SetLocal(code, result);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        GetLocal(code, right);
        references.Emit(code, ReferenceComparison.EqualZero);
        BranchIf(code, 0);
        GetLocal(code, right);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        WriteI32(code, boxed.TypeId);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        BranchIf(code, 0);

        foreach (var field in fields.Plan(type))
        {
            EmitFieldComparison(
                code,
                field,
                left,
                receiverKind,
                right,
                payloadOffset,
                functionIndices.Resolve(_objectEquals.Value));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            BranchIf(code, 0);
        }

        WriteI32(code, 1);
        SetLocal(code, result);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        GetLocal(code, result);
        SetLocal(code, output);
    }

    private void EmitFieldComparison(
        IWasmInstructionWriter code,
        ValueTypeEqualityField field,
        int left,
        CliValueKind receiverKind,
        int right,
        int payloadOffset,
        int objectEquals)
    {
        var leftOffset = receiverKind == CliValueKind.ManagedReference
            ? checked(payloadOffset + field.Offset)
            : field.Offset;
        var rightOffset = checked(payloadOffset + field.Offset);
        if (!field.Type.IsValueType)
        {
            GetLocal(code, left);
            ManagedMemoryEmitter.EmitReferenceLoad(
                code,
                layouts.Target,
                leftOffset);
            GetLocal(code, right);
            ManagedMemoryEmitter.EmitReferenceLoad(
                code,
                layouts.Target,
                rightOffset);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)objectEquals)));
            return;
        }

        if (field.Type.StackKind == CliValueKind.F4)
        {
            EmitFloatingComparison(
                code,
                field.Type,
                left,
                leftOffset,
                right,
                rightOffset,
                WasmOpcodes.F32Equal);
            return;
        }
        if (field.Type.StackKind == CliValueKind.F8)
        {
            EmitFloatingComparison(
                code,
                field.Type,
                left,
                leftOffset,
                right,
                rightOffset,
                WasmOpcodes.F64Equal);
            return;
        }

        var size = values.GetValueLayout(field.Type).Size;
        EmitLoad(code, left, leftOffset, field.Type);
        EmitLoad(code, right, rightOffset, field.Type);
        code.Write(WasmInstruction.NoOperand(
            ManagedMemoryEmitter.GetLoadValueType(
                field.Type,
                size) == WasmValueType.I64
                ? WasmOpcodes.I64Equal
                : WasmOpcodes.I32Equal));
    }

    private void EmitFloatingComparison(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int left,
        int leftOffset,
        int right,
        int rightOffset,
        byte equal)
    {
        EmitLoad(code, left, leftOffset, type);
        EmitLoad(code, right, rightOffset, type);
        code.Write(WasmInstruction.NoOperand(equal));
        EmitLoad(code, left, leftOffset, type);
        EmitLoad(code, left, leftOffset, type);
        code.Write(WasmInstruction.NoOperand(equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        EmitLoad(code, right, rightOffset, type);
        EmitLoad(code, right, rightOffset, type);
        code.Write(WasmInstruction.NoOperand(equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
    }

    private void EmitLoad(
        IWasmInstructionWriter code,
        int source,
        int offset,
        CliTypeIdentity type)
    {
        GetLocal(code, source);
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            offset,
            type,
            values.GetValueLayout(type).Size);
    }

    private static EntityKey ResolveObjectEquals(
        ITypeDescriptorSource descriptors,
        ITypeRepository types,
        IMethodRepository methods)
    {
        var objectType = descriptors.TypeDescriptors
            .Select(descriptor => types.GetTypeDefinition(descriptor.Type))
            .Single(type => type.FullName == "System.Object");
        return objectType.Methods
            .Select(methods.GetMethod)
            .Single(method =>
                method.Name == "Equals" &&
                method.IsStatic &&
                method.Signature.ParameterTypes.Length == 2)
            .Key;
    }

    private static void GetLocal(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void SetLocal(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void BranchIf(IWasmInstructionWriter code, uint depth) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(depth)));
}
