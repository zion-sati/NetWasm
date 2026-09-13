using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumToObjectEmitter(
    IEnumMetadataSource metadata,
    ITypeDescriptorSource descriptors,
    ITypeRepository types,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses,
    ITargetLayout layouts,
    IEnumTypeArgumentValidator typeArguments) : IEnumToObjectEmitter
{
    public void EmitToObject(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var argumentType = request.Method.Signature.ParameterTypes[1];
        var input = request.Local(1, argumentType);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, typeId);
        if (argumentType == CliValueKind.ManagedReference)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)input)));
            addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.ArgumentNull);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        addresses.Emit(code, 0);
        Set(code, result);
        foreach (var entry in metadata.EnumMetadata)
        {
            var underlying = entry.UnderlyingType;
            var valueLayout = values.GetValueLayout(underlying);
            var objectLayout = typeLayouts.GetObjectLayout(entry.Type);
            var payload = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, valueLayout.Alignment);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)typeId)));
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            var boxedValue = request.Local(1, underlying.StackKind);
            if (argumentType == CliValueKind.ManagedReference)
            {
                EmitBoxedValue(code, input, underlying, boxedValue);
            }
            addresses.Emit(code, objectLayout.Size);
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.Allocate))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)result)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)result)));
            addresses.Emit(code, payload);
            addresses.Emit(code, AddressOperation.Add);
            if (argumentType == CliValueKind.ManagedReference)
            {
                Get(code, boxedValue);
            }
            else
            {
                Get(code, input);
            }
            ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, 0, valueLayout.Size);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void EmitBoxedValue(
        IWasmInstructionWriter code,
        int input,
        CliTypeIdentity targetType,
        int valueLocal)
    {
        var sources = GetBoxedNumericSources();
        foreach (var source in sources)
        {
            Get(code, input);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            WriteI32(code, source.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Get(code, input);
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                source.PayloadOffset,
                source.Type,
                source.Size);
            EmitIntegerConversion(code, source.Type, targetType);
            Set(code, valueLocal);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        for (var index = 0; index < sources.Count; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private List<BoxedNumericSource> GetBoxedNumericSources()
    {
        var sources = new List<BoxedNumericSource>();
        foreach (var descriptor in descriptors.TypeDescriptors)
        {
            var type = PrimitiveNumericType(types.GetTypeDefinition(descriptor.Type).FullName);
            if (type is null)
            {
                continue;
            }
            var valueLayout = values.GetValueLayout(type);
            sources.Add(new(
                descriptor.TypeId,
                type,
                valueLayout.Size,
                WasmTargetLayout.Align(
                    layouts.Target.ObjectHeaderSize,
                    valueLayout.Alignment)));
        }
        foreach (var entry in metadata.EnumMetadata)
        {
            var valueLayout = values.GetValueLayout(entry.UnderlyingType);
            sources.Add(new(
                entry.TypeId,
                entry.UnderlyingType,
                valueLayout.Size,
                WasmTargetLayout.Align(
                    layouts.Target.ObjectHeaderSize,
                    valueLayout.Alignment)));
        }
        return sources;
    }

    private static CliTypeIdentity? PrimitiveNumericType(string fullName) => fullName switch
    {
        "System.Boolean" => CliTypeIdentity.Primitive("bool", CliValueKind.I4),
        "System.Char" => CliTypeIdentity.Primitive("char", CliValueKind.I4),
        "System.SByte" => CliTypeIdentity.Primitive("i1", CliValueKind.I4),
        "System.Byte" => CliTypeIdentity.Primitive("u1", CliValueKind.I4),
        "System.Int16" => CliTypeIdentity.Primitive("i2", CliValueKind.I4),
        "System.UInt16" => CliTypeIdentity.Primitive("u2", CliValueKind.I4),
        "System.Int32" => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        "System.UInt32" => CliTypeIdentity.Primitive("u4", CliValueKind.I4),
        "System.Int64" => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        "System.UInt64" => CliTypeIdentity.Primitive("u8", CliValueKind.I8),
        _ => null,
    };

    private static void EmitIntegerConversion(
        IWasmInstructionWriter code,
        CliTypeIdentity source,
        CliTypeIdentity target)
    {
        if (source.StackKind == target.StackKind)
        {
            return;
        }
        code.Write(WasmInstruction.NoOperand(
            target.StackKind == CliValueKind.I8
                ? IsUnsigned(source)
                    ? WasmOpcodes.I64ExtendI32Unsigned
                    : WasmOpcodes.I64ExtendI32Signed
                : WasmOpcodes.I32WrapI64));
    }

    private static bool IsUnsigned(CliTypeIdentity type) => type.CanonicalName is
        "primitive:bool" or "primitive:char" or "primitive:u1" or
        "primitive:u2" or "primitive:u4" or "primitive:u8";

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));

    private readonly record struct BoxedNumericSource(
        int TypeId,
        CliTypeIdentity Type,
        int Size,
        int PayloadOffset);
}
