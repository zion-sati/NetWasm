using System;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticReferenceBitmapBuilder : IStaticReferenceBitmapBuilder
{
    public byte[] Build(
        ImmutableArray<int> referenceOffsets,
        int bitCount,
        int referenceWordSize)
    {
        var wordBitCount = checked(referenceWordSize * 8);
        var wordCount = DivideRoundUp(bitCount, wordBitCount);
        var result = new byte[checked(wordCount * referenceWordSize)];
        foreach (var offset in referenceOffsets)
        {
            var bit = offset / referenceWordSize;
            var word = bit / wordBitCount;
            var position = bit % wordBitCount;
            var byteOffset = checked(word * referenceWordSize);
            switch (referenceWordSize)
            {
                case sizeof(uint):
                    var uintValue = BinaryPrimitives.ReadUInt32LittleEndian(
                        result.AsSpan(byteOffset));
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        result.AsSpan(byteOffset), uintValue | (1u << position));
                    break;
                case sizeof(ulong):
                    var ulongValue = BinaryPrimitives.ReadUInt64LittleEndian(
                        result.AsSpan(byteOffset));
                    BinaryPrimitives.WriteUInt64LittleEndian(
                        result.AsSpan(byteOffset), ulongValue | (1UL << position));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(referenceWordSize),
                        referenceWordSize,
                        "Only 32-bit and 64-bit reference words are supported.");
            }
        }
        return result;
    }

    private static int DivideRoundUp(int value, int divisor)
    {
        checked
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
