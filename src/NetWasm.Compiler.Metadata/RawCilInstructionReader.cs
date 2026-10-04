using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Metadata;

internal readonly record struct RawCilInstruction(int Offset, OpCode OpCode, int? Token)
{
    public ImmutableArray<int> BranchTargets { get; init; } = [];
}

internal interface IRawCilInstructionReader
{
    ImmutableArray<RawCilInstruction> Read(ReadOnlySpan<byte> bytes);
}

// Adapter: inspect operand boundaries in valid CLI bodies without requiring the
// instructions to belong to NetWasm's reachable executable subset.
internal sealed class RawCilInstructionReader : IRawCilInstructionReader
{
    private static readonly ImmutableDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToImmutableDictionary(code => unchecked((ushort)code.Value));

    public ImmutableArray<RawCilInstruction> Read(ReadOnlySpan<byte> bytes)
    {
        var result = ImmutableArray.CreateBuilder<RawCilInstruction>();
        var position = 0;
        while (position < bytes.Length)
        {
            var offset = position;
            ushort value = bytes[position++];
            if (value == 0xfe)
            {
                Require(bytes, position, 1);
                value = (ushort)(0xfe00 | bytes[position++]);
            }
            if (!OpCodesByValue.TryGetValue(value, out var code))
            {
                throw new BadImageFormatException("Unknown CLI opcode while proving a readonly field.");
            }
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => SwitchSize(bytes, position),
                _ => 4,
            };
            Require(bytes, position, size);
            var token = code.OperandType is OperandType.InlineField or OperandType.InlineMethod or
                OperandType.InlineType or OperandType.InlineTok
                    ? BinaryPrimitives.ReadInt32LittleEndian(bytes[position..])
                    : (int?)null;
            result.Add(new(offset, code, token)
            {
                BranchTargets = ReadBranchTargets(code, bytes, position, size),
            });
            position += size;
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<int> ReadBranchTargets(OpCode code, ReadOnlySpan<byte> bytes,
        int position, int size)
    {
        var next = position + size;
        if (code.OperandType == OperandType.ShortInlineBrTarget)
            return [Target(next, unchecked((sbyte)bytes[position]))];
        if (code.OperandType == OperandType.InlineBrTarget)
            return [Target(next, BinaryPrimitives.ReadInt32LittleEndian(bytes[position..]))];
        if (code.OperandType != OperandType.InlineSwitch) return [];
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[position..]);
        var targets = ImmutableArray.CreateBuilder<int>(count);
        for (var index = 0; index < count; index++)
            targets.Add(Target(next, BinaryPrimitives.ReadInt32LittleEndian(bytes[(position + 4 + index * 4)..])));
        return targets.MoveToImmutable();
    }

    private static int Target(int next, int displacement)
    {
        var target = (long)next + displacement;
        if (target < 0 || target > int.MaxValue)
            throw new BadImageFormatException("Invalid CLI branch target while proving a readonly field.");
        return (int)target;
    }

    private static int SwitchSize(ReadOnlySpan<byte> bytes, int position)
    {
        Require(bytes, position, 4);
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[position..]);
        if (count < 0 || count > (bytes.Length - position - 4) / 4)
        {
            throw new BadImageFormatException("Invalid CLI switch operand while proving a readonly field.");
        }
        return 4 + count * 4;
    }

    private static void Require(ReadOnlySpan<byte> bytes, int position, int count)
    {
        if (count > bytes.Length - position)
        {
            throw new BadImageFormatException("Truncated CLI operand while proving a readonly field.");
        }
    }
}
