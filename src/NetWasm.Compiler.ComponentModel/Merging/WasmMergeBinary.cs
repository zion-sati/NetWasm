using System.Text;
using System;

namespace NetWasm.Compiler.ComponentModel.Merging;

// Positional reading of a core module's bytes, for the merger, which keeps offsets into each
// input to copy what it does not change.
internal static class WasmMergeRead
{
    public static byte Byte(ReadOnlySpan<byte> bytes, ref int at) =>
        at < bytes.Length ? bytes[at++] : throw Invalid("a core module is truncated");

    public static uint Unsigned(ReadOnlySpan<byte> bytes, ref int at)
    {
        var value = 0u;
        for (var shift = 0; ; shift += 7)
        {
            var next = Byte(bytes, ref at);
            if (shift == 28 && next > 15)
                throw Invalid("a core module holds an unsigned LEB128 value out of range");
            value |= (uint)(next & 0x7f) << shift;
            if ((next & 0x80) == 0) return value;
        }
    }

    // A LEB128 value of up to 64 bits, signed or not, which the merger copies as it is.
    public static void Skip(ReadOnlySpan<byte> bytes, ref int at)
    {
        for (var length = 1; (Byte(bytes, ref at) & 0x80) != 0; length++)
        {
            if (length == 10) throw Invalid("a core module holds a LEB128 value longer than 64 bits");
        }
    }

    public static ReadOnlySpan<byte> Bytes(ReadOnlySpan<byte> bytes, ref int at, uint count)
    {
        if (count > bytes.Length - at) throw Invalid("a core module is truncated");
        var result = bytes.Slice(at, (int)count);
        at += (int)count;
        return result;
    }

    public static string Name(ReadOnlySpan<byte> bytes, ref int at)
    {
        var name = Bytes(bytes, ref at, Unsigned(bytes, ref at));
        try
        {
            return new UTF8Encoding(false, true).GetString(name);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("a core module holds a name that is not valid UTF-8");
        }
    }

    // A value type: a typed reference names a type the merger would have to renumber.
    public static void ValueType(ReadOnlySpan<byte> bytes, ref int at)
    {
        if (Byte(bytes, ref at) is 0x63 or 0x64)
            throw Invalid("a core module uses typed references, which the merger does not renumber");
    }

    public static void Limits(ReadOnlySpan<byte> bytes, ref int at)
    {
        var flags = Byte(bytes, ref at);
        Skip(bytes, ref at);
        if ((flags & 1) != 0) Skip(bytes, ref at);
    }

    public static Core.CompilerException Invalid(string message) => ComponentException.Invalid(message);
}

// A growing byte buffer the merger writes the module into.
internal sealed class WasmMergeSink
{
    private byte[] _buffer;

    public WasmMergeSink(int capacity = 256)
    {
        _buffer = new byte[Math.Max(capacity, 16)];
    }

    public int Length { get; private set; }

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, Length);

    public void Write(ReadOnlySpan<byte> bytes)
    {
        Reserve(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(Length));
        Length += bytes.Length;
    }

    public void Byte(byte value)
    {
        Reserve(1);
        _buffer[Length++] = value;
    }

    public void Unsigned(uint value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            Byte(value == 0 ? next : (byte)(next | 0x80));
        }
        while (value != 0);
    }

    // A non-negative value as a signed LEB128, as a block type or heap type names a type.
    public void Signed(uint value)
    {
        while (value >= 0x40)
        {
            Byte((byte)(value & 0x7f | 0x80));
            value >>= 7;
        }
        Byte((byte)value);
    }

    public void Name(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        Unsigned((uint)bytes.Length);
        Write(bytes);
    }

    public void Section(byte id, WasmMergeSink payload)
    {
        Byte(id);
        Unsigned((uint)payload.Length);
        Write(payload.Written);
    }

    public void Clear() => Length = 0;

    public byte[] ToArray() => Written.ToArray();

    private void Reserve(int count)
    {
        if (Length + count <= _buffer.Length) return;
        Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + count));
    }
}
