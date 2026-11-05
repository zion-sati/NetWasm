using System;
using System.Buffers;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Units;

internal sealed class WasmIndexOperandRewriter : IWasmIndexOperandRewriter
{
    public byte[] Rewrite(
        ReadOnlySpan<byte> body,
        ImmutableArray<WasmIndexOperand> operands,
        IWasmIndexRemapping remapping)
    {
        ArgumentNullException.ThrowIfNull(remapping);
        var output = new ArrayBufferWriter<byte>(body.Length + 16);
        var copied = 0;
        Span<byte> encoded = stackalloc byte[5];
        foreach (var operand in operands)
        {
            if (operand.Offset < copied || operand.Offset + operand.Length > body.Length)
                throw new ArgumentException("The operands must be the body's, in order.", nameof(operands));
            output.Write(body[copied..operand.Offset]);
            var index = remapping.Remap(operand.Space, operand.Index);
            var length = operand.IsSigned33 ? EncodeSigned33(index, encoded) : EncodeUnsigned(index, encoded);
            output.Write(encoded[..length]);
            copied = operand.Offset + operand.Length;
        }
        output.Write(body[copied..]);
        return output.WrittenSpan.ToArray();
    }

    private static int EncodeUnsigned(uint value, Span<byte> destination)
    {
        var length = 0;
        do
        {
            var next = (byte)(value & 0x7F);
            value >>= 7;
            destination[length++] = value == 0 ? next : (byte)(next | 0x80);
        }
        while (value != 0);
        return length;
    }

    // A non-negative s33: the last byte must leave its sign bit (0x40) clear.
    private static int EncodeSigned33(uint value, Span<byte> destination)
    {
        var remaining = (long)value;
        var length = 0;
        while (true)
        {
            var next = (byte)(remaining & 0x7F);
            remaining >>= 7;
            if (remaining == 0 && (next & 0x40) == 0)
            {
                destination[length++] = next;
                return length;
            }
            destination[length++] = (byte)(next | 0x80);
        }
    }
}
