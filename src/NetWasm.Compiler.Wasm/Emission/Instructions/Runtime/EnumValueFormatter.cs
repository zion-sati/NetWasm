using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumValueFormatter(
    IValueLayoutProvider values,
    IStaticDataLayout strings,
    IRuntimeObjectLayout objects,
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IEnumNumericFormatter numericFormatter) : IEnumValueFormatter
{
    public void Emit(
        IWasmInstructionWriter code,
        EnumMetadataLayout metadata,
        CliTypeIdentity underlyingType,
        int valueLocal,
        int payloadOffset,
        int? formatLocal,
        int resultLocal,
        int rawLocal,
        int scratchLocal)
    {
        var layout = values.GetValueLayout(underlyingType);
        foreach (var candidate in Candidates(metadata, underlyingType))
        {
            Get(code, valueLocal);
            ManagedMemoryEmitter.EmitLoadByType(
                code, layouts.Target, payloadOffset, underlyingType, layout.Size);
            if (underlyingType.StackKind == CliValueKind.I8)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Constant,
                    WasmInstructionOperand.Signed64(
                        unchecked((long)candidate.RawValue))));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
            }
            else
            {
                WriteI32(code, unchecked((int)candidate.RawValue));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitFormat(code, formatLocal, resultLocal, candidate);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }

        Get(code, resultLocal);
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        numericFormatter.Emit(
            code,
            underlyingType,
            valueLocal,
            payloadOffset,
            formatLocal,
            resultLocal,
            rawLocal,
            scratchLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitFormat(
        IWasmInstructionWriter code,
        int? format,
        int result,
        FormatCandidate candidate)
    {
        if (format is null)
        {
            SetString(code, result, candidate.General);
            return;
        }

        Get(code, format.Value);
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        SetString(code, result, candidate.General);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitFormatCode(code, format.Value, result, candidate);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitFormatCode(
        IWasmInstructionWriter code,
        int format,
        int result,
        FormatCandidate candidate)
    {
        EmitFormatMatch(code, format, 'D');
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        SetString(code, result, candidate.Decimal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitFormatMatch(code, format, 'X');
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        SetString(code, result, candidate.Hex);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        SetString(code, result, candidate.General);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitFormatMatch(
        IWasmInstructionWriter code,
        int format,
        char character)
    {
        Get(code, format);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        WriteI32(code, character);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Get(code, format);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(1, (uint)objects.StringDataOffset)));
        WriteI32(code, char.ToLowerInvariant(character));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
    }

    private void SetString(
        IWasmInstructionWriter code,
        int result,
        string value)
    {
        addresses.Emit(code, strings.GetStringLayout(value).Address);
        Set(code, result);
    }

    private static Dictionary<ulong, FormatCandidate>.ValueCollection Candidates(
        EnumMetadataLayout metadata,
        CliTypeIdentity underlyingType)
    {
        var candidates = new Dictionary<ulong, FormatCandidate>();
        foreach (var member in metadata.Members)
        {
            candidates.TryAdd(member.RawValue, new(
                member.RawValue,
                member.Name,
                Decimal(member.RawValue, underlyingType),
                Hex(member.RawValue, underlyingType)));
        }
        var atoms = metadata.Members
            .Where(member => member.RawValue != 0 &&
                (member.RawValue & (member.RawValue - 1)) == 0)
            .ToArray();
        if (metadata.IsFlags && atoms.Length <= 10)
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
                candidates.TryAdd(raw, new(
                    raw,
                    string.Join(", ", names),
                    Decimal(raw, underlyingType),
                    Hex(raw, underlyingType)));
            }
        }
        return candidates.Values;
    }

    private static string Decimal(ulong raw, CliTypeIdentity type) =>
        type.CanonicalName switch
        {
            "primitive:i1" => unchecked((sbyte)raw).ToString(CultureInfo.InvariantCulture),
            "primitive:i2" => unchecked((short)raw).ToString(CultureInfo.InvariantCulture),
            "primitive:i4" => unchecked((int)raw).ToString(CultureInfo.InvariantCulture),
            "primitive:i8" => unchecked((long)raw).ToString(CultureInfo.InvariantCulture),
            _ => raw.ToString(CultureInfo.InvariantCulture),
        };

    private static string Hex(ulong raw, CliTypeIdentity type)
    {
        var width = type.CanonicalName switch
        {
            "primitive:i1" or "primitive:u1" => 2,
            "primitive:i2" or "primitive:u2" or "primitive:char" => 4,
            "primitive:i4" or "primitive:u4" => 8,
            _ => 16,
        };
        return raw.ToString($"X{width}", CultureInfo.InvariantCulture);
    }

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

    private readonly record struct FormatCandidate(
        ulong RawValue,
        string General,
        string Decimal,
        string Hex);
}
