// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23). Only synchronous writer members
// are retained; asynchronous disposal is implemented with the asynchronous I/O surface.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;

namespace System.IO;

public class BinaryWriter : IAsyncDisposable, IDisposable
{
    private const int MaxArrayPoolRentalSize = 64 * 1024;

    public static readonly BinaryWriter Null = new();

    protected Stream OutStream;
    private readonly Encoding _encoding;
    private readonly bool _leaveOpen;
    private readonly bool _useFastUtf8;

    protected BinaryWriter()
    {
        OutStream = Stream.Null;
        _encoding = Encoding.UTF8;
        _useFastUtf8 = true;
    }

    public BinaryWriter(Stream output) : this(output, Encoding.UTF8, leaveOpen: false) { }

    public BinaryWriter(Stream output, Encoding encoding) : this(output, encoding, leaveOpen: false) { }

    public BinaryWriter(Stream output, Encoding encoding, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(encoding);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The stream does not support writing.", nameof(output));
        }

        OutStream = output;
        _encoding = encoding;
        _leaveOpen = leaveOpen;
        _useFastUtf8 = encoding.CodePage == 65001 && encoding.EncoderFallback.MaxCharCount <= 1;
    }

    public virtual void Close() => Dispose(disposing: true);

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_leaveOpen)
            {
                OutStream.Flush();
            }
            else
            {
                OutStream.Close();
            }
        }
    }

    public void Dispose() => Dispose(disposing: true);

    public virtual ValueTask DisposeAsync()
    {
        try
        {
            if (GetType() == typeof(BinaryWriter))
            {
                if (_leaveOpen)
                {
                    return new ValueTask(OutStream.FlushAsync());
                }

                OutStream.Close();
            }
            else
            {
                Dispose();
            }

            return default;
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }
    }

    public virtual Stream BaseStream
    {
        get
        {
            Flush();
            return OutStream;
        }
    }

    public virtual void Flush() => OutStream.Flush();

    public virtual long Seek(int offset, SeekOrigin origin) => OutStream.Seek(offset, origin);

    public virtual void Write(bool value) => OutStream.WriteByte((byte)(value ? 1 : 0));

    public virtual void Write(byte value) => OutStream.WriteByte(value);

    public virtual void Write(sbyte value) => OutStream.WriteByte((byte)value);

    public virtual void Write(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        OutStream.Write(buffer, 0, buffer.Length);
    }

    public virtual void Write(byte[] buffer, int index, int count) => OutStream.Write(buffer, index, count);

    public virtual void Write(char value)
    {
        if (!Rune.TryCreate(value, out var rune))
        {
            throw new ArgumentException("A surrogate cannot be written as a single character.", nameof(value));
        }

        Span<byte> buffer = stackalloc byte[8];
        if (_useFastUtf8)
        {
            var byteCount = rune.EncodeToUtf8(buffer);
            OutStream.Write(buffer[..byteCount]);
            return;
        }

        byte[]? rented = null;
        var maxByteCount = _encoding.GetMaxByteCount(1);
        if (maxByteCount > buffer.Length)
        {
            rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
            buffer = rented;
        }

        var actualByteCount = _encoding.GetBytes(new ReadOnlySpan<char>(in value), buffer);
        OutStream.Write(buffer[..actualByteCount]);
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public virtual void Write(char[] chars)
    {
        ArgumentNullException.ThrowIfNull(chars);
        WriteCharsCommonWithoutLengthPrefix(chars, useThisWriteOverride: false);
    }

    public virtual void Write(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (index > chars.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "The index and count exceed the character array.");
        }

        WriteCharsCommonWithoutLengthPrefix(chars.AsSpan(index, count), useThisWriteOverride: false);
    }

    public virtual void Write(double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(decimal value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(decimal)];
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        for (var index = 0; index < bits.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(index * sizeof(int), sizeof(int)), bits[index]);
        }

        OutStream.Write(buffer);
    }

    public virtual void Write(short value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(Half value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteHalfLittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public virtual void Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (_useFastUtf8)
        {
            if (GetType() == typeof(BinaryWriter) && value.Length <= 127 / 3)
            {
                Span<byte> buffer = stackalloc byte[128];
                var byteCount = _encoding.GetBytes(value, buffer[1..]);
                buffer[0] = (byte)byteCount;
                OutStream.Write(buffer[..(byteCount + 1)]);
                return;
            }

            if (value.Length <= MaxArrayPoolRentalSize / 3)
            {
                var rented = ArrayPool<byte>.Shared.Rent(value.Length * 3);
                var byteCount = _encoding.GetBytes(value, rented);
                Write7BitEncodedInt(byteCount);
                OutStream.Write(rented, 0, byteCount);
                ArrayPool<byte>.Shared.Return(rented);
                return;
            }
        }

        var actualByteCount = _encoding.GetByteCount(value);
        Write7BitEncodedInt(actualByteCount);
        WriteCharsCommonWithoutLengthPrefix(value, useThisWriteOverride: false);
    }

    public virtual void Write(ReadOnlySpan<byte> buffer)
    {
        if (GetType() == typeof(BinaryWriter))
        {
            OutStream.Write(buffer);
            return;
        }

        var array = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(array);
            Write(array, 0, buffer.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }

    public virtual void Write(ReadOnlySpan<char> chars) =>
        WriteCharsCommonWithoutLengthPrefix(chars, useThisWriteOverride: true);

    private void WriteCharsCommonWithoutLengthPrefix(ReadOnlySpan<char> chars, bool useThisWriteOverride)
    {
        byte[] rented;
        if (chars.Length <= MaxArrayPoolRentalSize)
        {
            var maxByteCount = _encoding.GetMaxByteCount(chars.Length);
            if (maxByteCount <= MaxArrayPoolRentalSize)
            {
                rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
                var byteCount = _encoding.GetBytes(chars, rented);
                WriteToOutStream(rented, 0, byteCount, useThisWriteOverride);
                ArrayPool<byte>.Shared.Return(rented);
                return;
            }
        }

        rented = ArrayPool<byte>.Shared.Rent(MaxArrayPoolRentalSize);
        var encoder = _encoding.GetEncoder();
        bool completed;
        do
        {
            encoder.Convert(chars, rented, flush: true, out var charsConsumed, out var bytesWritten, out completed);
            if (bytesWritten != 0)
            {
                WriteToOutStream(rented, 0, bytesWritten, useThisWriteOverride);
            }

            chars = chars.Slice(charsConsumed);
        }
        while (!completed);

        ArrayPool<byte>.Shared.Return(rented);

        void WriteToOutStream(byte[] buffer, int offset, int count, bool useOverride)
        {
            if (useOverride)
            {
                Write(buffer, offset, count);
            }
            else
            {
                OutStream.Write(buffer, offset, count);
            }
        }
    }

    public void Write7BitEncodedInt(int value)
    {
        var unsignedValue = (uint)value;
        while (unsignedValue > 0x7Fu)
        {
            Write((byte)(unsignedValue | ~0x7Fu));
            unsignedValue >>= 7;
        }

        Write((byte)unsignedValue);
    }

    public void Write7BitEncodedInt64(long value)
    {
        var unsignedValue = (ulong)value;
        while (unsignedValue > 0x7Fu)
        {
            Write((byte)((uint)unsignedValue | ~0x7Fu));
            unsignedValue >>= 7;
        }

        Write((byte)unsignedValue);
    }
}
