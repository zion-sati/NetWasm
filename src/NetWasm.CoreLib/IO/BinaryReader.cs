// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23). Async members and runtime-private
// MemoryStream fast paths are intentionally omitted for the NetWasm profile.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace System.IO;

public class BinaryReader : IDisposable
{
    private const int MaxCharBytesSize = 128;

    private readonly Stream _stream;
    private readonly Encoding _encoding;
    private Decoder? _decoder;
    private char[]? _charBuffer;
    private readonly int _maxCharsSize;
    private readonly bool _twoBytesPerChar;
    private readonly bool _leaveOpen;
    private bool _disposed;

    public BinaryReader(Stream input) : this(input, Encoding.UTF8, leaveOpen: false) { }

    public BinaryReader(Stream input, Encoding encoding) : this(input, encoding, leaveOpen: false) { }

    public BinaryReader(Stream input, Encoding encoding, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(encoding);
        if (!input.CanRead)
        {
            throw new ArgumentException("The stream does not support reading.", nameof(input));
        }

        _stream = input;
        _encoding = encoding;
        _maxCharsSize = encoding.GetMaxCharCount(MaxCharBytesSize);
        _twoBytesPerChar = encoding is UnicodeEncoding;
        _leaveOpen = leaveOpen;
    }

    public virtual Stream BaseStream => _stream;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing && !_leaveOpen)
            {
                _stream.Close();
            }

            _disposed = true;
        }
    }

    public void Dispose() => Dispose(disposing: true);

    public virtual void Close() => Dispose(disposing: true);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BinaryReader));
        }
    }

    public virtual int PeekChar()
    {
        ThrowIfDisposed();
        if (!_stream.CanSeek)
        {
            return -1;
        }

        var originalPosition = _stream.Position;
        var character = Read();
        _stream.Position = originalPosition;
        return character;
    }

    public virtual int Read()
    {
        ThrowIfDisposed();

        var charactersRead = 0;
        var savedPosition = 0L;
        if (_stream.CanSeek)
        {
            savedPosition = _stream.Position;
        }

        _decoder ??= _encoding.GetDecoder();
        Span<byte> characterBytes = stackalloc byte[MaxCharBytesSize];
        var singleCharacter = '\0';

        while (charactersRead == 0)
        {
            var bytesToDecode = _twoBytesPerChar ? 2 : 1;
            var read = _stream.ReadByte();
            if (read == -1)
            {
                bytesToDecode = 0;
            }
            else
            {
                characterBytes[0] = (byte)read;
            }

            if (bytesToDecode == 2)
            {
                read = _stream.ReadByte();
                if (read == -1)
                {
                    bytesToDecode = 1;
                }
                else
                {
                    characterBytes[1] = (byte)read;
                }
            }

            if (bytesToDecode == 0)
            {
                return -1;
            }

            try
            {
                charactersRead = _decoder.GetChars(characterBytes[..bytesToDecode], new Span<char>(ref singleCharacter), flush: false);
            }
            catch
            {
                if (_stream.CanSeek)
                {
                    _stream.Seek(savedPosition - _stream.Position, SeekOrigin.Current);
                }

                throw;
            }
        }

        return singleCharacter;
    }

    public virtual byte ReadByte() => InternalReadByte();

    private byte InternalReadByte()
    {
        ThrowIfDisposed();
        var value = _stream.ReadByte();
        if (value == -1)
        {
            throw new EndOfStreamException();
        }

        return (byte)value;
    }

    public virtual sbyte ReadSByte() => (sbyte)InternalReadByte();

    public virtual bool ReadBoolean() => InternalReadByte() != 0;

    public virtual char ReadChar()
    {
        var value = Read();
        if (value == -1)
        {
            throw new EndOfStreamException();
        }

        return (char)value;
    }

    public virtual short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(InternalRead(stackalloc byte[sizeof(short)]));
    public virtual ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(InternalRead(stackalloc byte[sizeof(ushort)]));
    public virtual int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(InternalRead(stackalloc byte[sizeof(int)]));
    public virtual uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(InternalRead(stackalloc byte[sizeof(uint)]));
    public virtual long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(InternalRead(stackalloc byte[sizeof(long)]));
    public virtual ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(InternalRead(stackalloc byte[sizeof(ulong)]));
    public virtual Half ReadHalf() => BinaryPrimitives.ReadHalfLittleEndian(InternalRead(stackalloc byte[sizeof(ushort)]));
    public virtual float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(InternalRead(stackalloc byte[sizeof(float)]));
    public virtual double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(InternalRead(stackalloc byte[sizeof(double)]));

    public virtual decimal ReadDecimal()
    {
        var data = InternalRead(stackalloc byte[sizeof(decimal)]);
        Span<int> bits = stackalloc int[4];
        for (var index = 0; index < bits.Length; index++)
        {
            bits[index] = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(index * sizeof(int), sizeof(int)));
        }

        try
        {
            return new decimal(bits);
        }
        catch (ArgumentException exception)
        {
            throw new IOException("The binary decimal value is invalid.", exception);
        }
    }

    public virtual string ReadString()
    {
        ThrowIfDisposed();
        var stringLength = Read7BitEncodedInt();
        if (stringLength < 0)
        {
            throw new IOException("The binary string length is invalid.");
        }

        if (stringLength == 0)
        {
            return string.Empty;
        }

        Span<byte> characterBytes = stackalloc byte[MaxCharBytesSize];
        var currentPosition = 0;
        StringBuilder? builder = null;
        do
        {
            var readLength = Math.Min(MaxCharBytesSize, stringLength - currentPosition);
            var read = _stream.Read(characterBytes[..readLength]);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            if (currentPosition == 0 && read == stringLength)
            {
                return _encoding.GetString(characterBytes[..read]);
            }

            _decoder ??= _encoding.GetDecoder();
            _charBuffer ??= new char[_maxCharsSize];
            var charsRead = _decoder.GetChars(characterBytes[..read], _charBuffer, flush: false);
            builder ??= new StringBuilder(Math.Min(stringLength, 1024));
            builder.Append(_charBuffer, 0, charsRead);
            currentPosition += read;
        }
        while (currentPosition < stringLength);

        return builder!.ToString();
    }

    public virtual int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - index < count)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.");
        }

        ThrowIfDisposed();
        return InternalReadChars(new Span<char>(buffer, index, count));
    }

    public virtual int Read(Span<char> buffer)
    {
        ThrowIfDisposed();
        return InternalReadChars(buffer);
    }

    private int InternalReadChars(Span<char> buffer)
    {
        _decoder ??= _encoding.GetDecoder();
        var totalCharsRead = 0;
        Span<byte> characterBytes = stackalloc byte[MaxCharBytesSize];

        while (!buffer.IsEmpty)
        {
            var bytesToRead = buffer.Length * (_twoBytesPerChar ? 2 : 1);
            if (bytesToRead > 1)
            {
                bytesToRead--;
            }

            bytesToRead = Math.Min(bytesToRead, MaxCharBytesSize);
            var read = _stream.Read(characterBytes[..bytesToRead]);
            if (read == 0)
            {
                break;
            }

            var charsRead = _decoder.GetChars(characterBytes[..read], buffer, flush: false);
            buffer = buffer.Slice(charsRead);
            totalCharsRead += charsRead;
        }

        return totalCharsRead;
    }

    public virtual char[] ReadChars(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ThrowIfDisposed();
        if (count == 0)
        {
            return [];
        }

        var chars = new char[count];
        var read = InternalReadChars(chars);
        return read == count ? chars : chars[..read];
    }

    public virtual int Read(byte[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - index < count)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.");
        }

        ThrowIfDisposed();
        return _stream.Read(buffer, index, count);
    }

    public virtual int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        return _stream.Read(buffer);
    }

    public virtual byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ThrowIfDisposed();
        if (count == 0)
        {
            return [];
        }

        var result = new byte[count];
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = _stream.Read(result, totalRead, count - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead == count ? result : result[..totalRead];
    }

    public virtual void ReadExactly(Span<byte> buffer)
    {
        ThrowIfDisposed();
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = _stream.Read(buffer.Slice(totalRead));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            totalRead += read;
        }
    }

    protected virtual void FillBuffer(int numBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numBytes);
        ThrowIfDisposed();
        if (numBytes == 0)
        {
            if (_stream.Read(Span<byte>.Empty) == 0)
            {
                throw new EndOfStreamException();
            }

            return;
        }

        if (_stream.CanSeek)
        {
            var original = _stream.Position;
            _stream.Seek(numBytes, SeekOrigin.Current);
            if (_stream.Position - original != numBytes)
            {
                throw new EndOfStreamException();
            }

            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(numBytes);
        try
        {
            ReadExactly(buffer.AsSpan(0, numBytes));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private ReadOnlySpan<byte> InternalRead(Span<byte> buffer)
    {
        ReadExactly(buffer);
        return buffer;
    }

    public int Read7BitEncodedInt()
    {
        uint result = 0;
        const int maxBytesWithoutOverflow = 4;
        for (var shift = 0; shift < maxBytesWithoutOverflow * 7; shift += 7)
        {
            var byteRead = ReadByte();
            result |= (uint)(byteRead & 0x7F) << shift;
            if (byteRead <= 0x7F)
            {
                return (int)result;
            }
        }

        var finalByte = ReadByte();
        if (finalByte > 0x0F)
        {
            throw new FormatException("The encoded integer is invalid.");
        }

        result |= (uint)finalByte << (maxBytesWithoutOverflow * 7);
        return (int)result;
    }

    public long Read7BitEncodedInt64()
    {
        ulong result = 0;
        const int maxBytesWithoutOverflow = 9;
        for (var shift = 0; shift < maxBytesWithoutOverflow * 7; shift += 7)
        {
            var byteRead = ReadByte();
            result |= (ulong)(byteRead & 0x7F) << shift;
            if (byteRead <= 0x7F)
            {
                return (long)result;
            }
        }

        var finalByte = ReadByte();
        if (finalByte > 1)
        {
            throw new FormatException("The encoded integer is invalid.");
        }

        result |= (ulong)finalByte << (maxBytesWithoutOverflow * 7);
        return (long)result;
    }
}
