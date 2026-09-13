using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumParseEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    IRuntimeObjectLayout objects,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses,
    ITargetLayout layouts,
    IEnumNumericParseEmitter numericParser,
    IEnumTypeArgumentValidator typeArguments,
    IEnumValueBoxEmitter boxes) : IEnumParseEmitter
{
    public void EmitParse(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 0)
        {
            if (request.Method.Signature.ParameterTypes.Length is not (3 or 4))
                throw new InvalidOperationException(
                    "enum parse intrinsic requires one enum type argument");
            EmitTypeBased(request, code);
            return;
        }
        if (request.Method.MethodArguments.Length != 1)
            throw new InvalidOperationException(
                "enum parse intrinsic requires one enum type argument");

        var enumType = request.Method.MethodArguments[0];
        var entry = metadata.EnumMetadata
            .Where(candidate => candidate.Type.Assembly.Equals(enumType.Assembly))
            .SingleOrDefault(candidate =>
                types.GetTypeDefinition(candidate.Type).FullName == enumType.FullName);
        if (entry.TypeId == 0)
            throw new InvalidOperationException(
                $"enum metadata is unavailable for '{enumType.CanonicalName}'");

        EmitEntry(request, code, entry, typeBased: false);
    }

    private void EmitTypeBased(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var context = request.Instruction.Context;
        var type = request.Local(0, CliValueKind.ManagedReference);
        typeArguments.Validate(code, type, context.NumericTemporaryI4);
        addresses.Emit(code, 0);
        Set(code, context.ObjectTemporary);
        foreach (var entry in metadata.EnumMetadata)
        {
            Get(code, context.NumericTemporaryI4);
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitEntry(request, code, entry, typeBased: true);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        if (request.Method.Definition.Name != "InternalTryParse")
        {
            Get(code, context.ObjectTemporary);
            Set(code, request.Local(0, CliValueKind.ManagedReference));
        }
    }

    private void EmitEntry(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        EnumMetadataLayout entry,
        bool typeBased)
    {

        var underlyingType = entry.UnderlyingType;
        var context = request.Instruction.Context;
        var argumentOffset = typeBased ? 1 : 0;
        var text = request.Local(argumentOffset, CliValueKind.ManagedReference);
        var isArray = request.Method.Signature.ParameterSignatureTypes[argumentOffset].Shape is
            CliTypeShape.SzArray or CliTypeShape.Array;
        var ignoreCase = request.Local(argumentOffset + 1, CliValueKind.I4);
        var matched = typeBased
            ? context.NumericTemporaryI4Third
            : context.NumericTemporaryI4Second;
        var parsed = underlyingType.StackKind == CliValueKind.I8
            ? context.NumericTemporaryI8
            : typeBased
                ? context.NumericTemporaryI4Second
                : context.NumericTemporaryI4;
        var tryParse = request.Method.Definition.Name == "InternalTryParse";
        var outValue = tryParse
            ? request.Local(argumentOffset + 2, CliValueKind.ManagedAddress)
            : -1;

        WriteI32(code, 0);
        Set(code, matched);
        if (tryParse && !typeBased)
            StoreValue(code, underlyingType, outValue, 0);
        else if (tryParse)
        {
            Get(code, outValue);
            addresses.Emit(code, 0);
            ManagedMemoryEmitter.EmitStoreBySize(
                code,
                layouts.Target,
                0,
                layouts.Target.AddressSize);
        }

        numericParser.Emit(
            new EnumNumericParseEmissionRequest(
                underlyingType,
                text,
                new EnumNumericParseOutput(parsed, matched),
                typeBased
                    ? context.NumericTemporaryI4Fourth
                    : context.NumericTemporaryI4Third,
                typeBased
                    ? context.NumericTemporaryI4Fifth
                    : context.NumericTemporaryI4Fourth,
                isArray),
            code);

        Get(code, matched);
        WriteI32(code, (int)EnumNumericParseStatus.NonNumeric);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, 0);
        Set(code, matched);
        foreach (var candidate in Candidates(entry, underlyingType))
        {
            Get(code, matched);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitStringMatch(code, text, ignoreCase, candidate.Text, matched, isArray);
            Get(code, matched);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            StoreLocal(code, underlyingType, parsed, candidate.RawValue);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        Get(code, matched);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, (int)EnumNumericParseStatus.Success);
        Set(code, matched);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteI32(code, (int)EnumNumericParseStatus.NonNumeric);
        Set(code, matched);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, matched);
        WriteI32(code, (int)EnumNumericParseStatus.Success);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (tryParse)
        {
            if (typeBased)
            {
                boxes.Emit(code, entry, parsed, context.ObjectTemporary);
                Get(code, outValue);
                Get(code, context.ObjectTemporary);
                ManagedMemoryEmitter.EmitStoreBySize(
                    code,
                    layouts.Target,
                    0,
                    layouts.Target.AddressSize);
            }
            else
            {
                StoreParsedValue(code, underlyingType, outValue, parsed);
            }
        }
        else
        {
            if (typeBased)
            {
                boxes.Emit(code, entry, parsed, context.ObjectTemporary);
            }
            else
            {
                Get(code, parsed);
                Set(code, request.Local(0, underlyingType.StackKind));
            }
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        if (!tryParse)
        {
            Get(code, matched);
            WriteI32(code, (int)EnumNumericParseStatus.Overflow);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.Overflow);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            exceptions.Emit(code, ManagedExceptionKind.Argument);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        if (tryParse)
        {
            Get(code, matched);
            WriteI32(code, (int)EnumNumericParseStatus.Success);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            Set(code, request.Local(0, CliValueKind.I4));
        }
    }

    private void StoreParsedValue(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int address,
        int value)
    {
        Get(code, address);
        Get(code, value);
        var size = type.StackKind == CliValueKind.I8
            ? 8
            : type.CanonicalName is "primitive:i1" or "primitive:u1"
                ? 1
                : type.CanonicalName is "primitive:i2" or "primitive:u2" or "primitive:char"
                    ? 2
                    : 4;
        ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, 0, size);
    }

    private void EmitStringMatch(
        IWasmInstructionWriter code,
        int text,
        int ignoreCase,
        string expected,
        int matched,
        bool isArray)
    {
        WriteI32(code, 0);
        Set(code, matched);
        Get(code, text);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                2,
                (uint)(isArray ? objects.ArrayLengthOffset : objects.StringLengthOffset))));
        WriteI32(code, expected.Length);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        WriteI32(code, 1);
        Set(code, matched);
        for (var index = 0; index < expected.Length; index++)
        {
            Get(code, matched);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Get(code, ignoreCase);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitCharacter(code, text, index, isArray);
            WriteI32(code, 32);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
            WriteI32(code, expected[index] | 32);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            Set(code, matched);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            EmitCharacter(code, text, index, isArray);
            WriteI32(code, expected[index]);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            Set(code, matched);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitCharacter(
        IWasmInstructionWriter code,
        int text,
        int index,
        bool isArray = false)
    {
        Get(code, text);
        if (isArray)
        {
            ManagedMemoryEmitter.EmitReferenceLoad(
                code,
                layouts.Target,
                objects.ArrayDataPointerOffset);
        }
        if (layouts.Target.UsesMemory64)
        {
            WriteI32(code, index * sizeof(char));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            WriteI32(code, index * sizeof(char));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(
                1,
                isArray ? 0u : (uint)objects.StringDataOffset)));
    }

    private static IEnumerable<Candidate> Candidates(
        EnumMetadataLayout entry,
        CliTypeIdentity enumType)
    {
        var candidates = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var member in entry.Members)
        {
            candidates.TryAdd(member.Name, member.RawValue);
            candidates.TryAdd(ToDecimal(member.RawValue, enumType.CanonicalName), member.RawValue);
        }
        var atoms = entry.Members.Where(member => member.RawValue != 0 &&
            (member.RawValue & (member.RawValue - 1)) == 0).ToArray();
        if (entry.IsFlags && atoms.Length <= 10)
        {
            for (var mask = 1; mask < (1 << atoms.Length); mask++)
            {
                ulong raw = 0;
                var names = new List<string>();
                for (var index = 0; index < atoms.Length; index++)
                {
                    if ((mask & (1 << index)) == 0)
                        continue;
                    raw |= atoms[index].RawValue;
                    names.Add(atoms[index].Name);
                }
                candidates.TryAdd(string.Join(", ", names), raw);
                candidates.TryAdd(ToDecimal(raw, enumType.CanonicalName), raw);
            }
        }
        foreach (var pair in candidates.ToArray())
        {
            if (pair.Key.Contains(','))
                candidates.TryAdd("  " + pair.Key + "  ", pair.Value);
        }
        return candidates.Select(pair => new Candidate(pair.Key, pair.Value));
    }

    private static string ToDecimal(ulong raw, string canonicalName) =>
        canonicalName switch
        {
            "primitive:i1" => unchecked((sbyte)raw).ToString(CultureInfo.InvariantCulture),
            "primitive:i2" => unchecked((short)raw).ToString(CultureInfo.InvariantCulture),
            "primitive:i4" => unchecked((int)raw).ToString(CultureInfo.InvariantCulture),
            "primitive:i8" => unchecked((long)raw).ToString(CultureInfo.InvariantCulture),
            _ => raw.ToString(CultureInfo.InvariantCulture),
        };

    private void StoreValue(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int address,
        ulong raw)
    {
        Get(code, address);
        StoreLocalValue(code, type, raw);
        var size = type.StackKind == CliValueKind.I8
            ? 8
            : type.CanonicalName is "primitive:i1" or "primitive:u1"
                ? 1
                : type.CanonicalName is "primitive:i2" or "primitive:u2" or "primitive:char"
                    ? 2
                    : 4;
        ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, 0, size);
    }

    private static void StoreLocal(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int local,
        ulong raw)
    {
        StoreLocalValue(code, type, raw);
        Set(code, local);
    }

    private static void StoreLocalValue(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        ulong raw) => code.Write(WasmInstruction.WithOperand(
            type.StackKind == CliValueKind.I8
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant,
            type.StackKind == CliValueKind.I8
                ? WasmInstructionOperand.Signed64(unchecked((long)raw))
                : WasmInstructionOperand.Signed(unchecked((int)raw))));

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private readonly record struct Candidate(string Text, ulong RawValue);
}
