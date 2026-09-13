using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumIsDefinedEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    IValueLayoutProvider values,
    ITargetLayout layouts,
    IEnumTypeArgumentValidator typeArguments) : IEnumIsDefinedEmitter
{
    public void EmitIsDefined(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 0)
        {
            EmitTypeBased(request, code);
            return;
        }
        if (request.Method.MethodArguments.Length != 1)
            throw new InvalidOperationException("enum is-defined intrinsic requires one enum type argument");
        var enumType = request.Method.MethodArguments[0];
        var entry = metadata.EnumMetadata
            .Where(candidate => candidate.Type.Assembly.Equals(enumType.Assembly))
            .SingleOrDefault(candidate => types.GetTypeDefinition(candidate.Type).FullName == enumType.FullName);
        if (entry.TypeId == 0)
            throw new InvalidOperationException($"enum metadata is unavailable for '{enumType.CanonicalName}'");

        var input = request.Local(0, enumType.StackKind);
        var result = request.Local(0, CliValueKind.I4);
        var preservedInput = enumType.StackKind == CliValueKind.I8
            ? request.Instruction.Context.NumericTemporaryI8
            : request.Instruction.Context.NumericTemporaryI4;
        Get(code, input);
        Set(code, preservedInput);
        WriteI32(code, 0);
        Set(code, result);
        foreach (var member in entry.Members)
        {
            Get(code, preservedInput);
            if (enumType.StackKind == CliValueKind.I8)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Constant,
                    WasmInstructionOperand.Signed64(unchecked((long)member.RawValue))));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
            }
            else
            {
                WriteI32(code, unchecked((int)member.RawValue));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            WriteI32(code, 1);
            Set(code, result);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        Get(code, result);
        static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        static void Get(IWasmInstructionWriter code, int local) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
        static void Set(IWasmInstructionWriter code, int local) => code.Write(
            WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    }

    private void EmitTypeBased(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Local(0, CliValueKind.ManagedReference);
        var value = request.Local(1, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.I4);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, typeId);
        WriteI32(code, 0);
        Set(code, result);
        foreach (var entry in metadata.EnumMetadata)
        {
            var underlying = types.GetTypeDefinition(entry.Type).EnumUnderlyingType;
            var layout = values.GetValueLayout(underlying);
            var payload = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, layout.Alignment);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)typeId)));
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            foreach (var member in entry.Members)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)value)));
                ManagedMemoryEmitter.EmitLoadByType(code, layouts.Target, payload, underlying, layout.Size);
                if (underlying.StackKind == CliValueKind.I8)
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant,
                        WasmInstructionOperand.Signed64(unchecked((long)member.RawValue))));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
                }
                else
                {
                    WriteI32(code, unchecked((int)member.RawValue));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                }
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                    WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                WriteI32(code, 1);
                Set(code, result);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));


    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
