// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23); asynchronous members omitted.

namespace System.IO
{
    public class StringReader : TextReader
    {
        private string? _value;
        private int _position;

        public StringReader(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _value = value;
        }

        public override void Close() => Dispose(true);

        protected override void Dispose(bool disposing)
        {
            _value = null;
            _position = 0;
            base.Dispose(disposing);
        }

        public override int Peek()
        {
            var value = _value ?? throw new ObjectDisposedException(null, "Cannot read from a closed reader.");
            return (uint)_position < (uint)value.Length ? value[_position] : -1;
        }

        public override int Read()
        {
            var value = _value ?? throw new ObjectDisposedException(null, "Cannot read from a closed reader.");
            if ((uint)_position >= (uint)value.Length)
            {
                return -1;
            }

            return value[_position++];
        }

        public override int Read(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            var value = _value ?? throw new ObjectDisposedException(null, "Cannot read from a closed reader.");
            var read = Math.Min(value.Length - _position, count);
            if (read > 0)
            {
                value.CopyTo(_position, buffer, index, read);
                _position += read;
            }

            return read;
        }

        public override int Read(Span<char> buffer)
        {
            if (GetType() != typeof(StringReader))
            {
                return base.Read(buffer);
            }

            var value = _value ?? throw new ObjectDisposedException(null, "Cannot read from a closed reader.");
            var read = Math.Min(value.Length - _position, buffer.Length);
            if (read > 0)
            {
                value.AsSpan(_position, read).CopyTo(buffer);
                _position += read;
            }

            return read;
        }

        public override int ReadBlock(Span<char> buffer) => Read(buffer);

        public override string ReadToEnd()
        {
            var value = _value ?? throw new ObjectDisposedException(null, "Cannot read from a closed reader.");
            var position = _position;
            _position = value.Length;
            return position == 0 ? value : value.Substring(position);
        }

        public override string? ReadLine()
        {
            var value = _value ?? throw new ObjectDisposedException(null, "Cannot read from a closed reader.");
            if ((uint)_position >= (uint)value.Length)
            {
                return null;
            }

            var start = _position;
            var position = start;
            while ((uint)position < (uint)value.Length && value[position] is not '\r' and not '\n')
            {
                position++;
            }

            if (position == value.Length)
            {
                _position = position;
                return value.Substring(start);
            }

            var result = value.Substring(start, position - start);
            if (value[position++] == '\r' && (uint)position < (uint)value.Length && value[position] == '\n')
            {
                position++;
            }

            _position = position;
            return result;
        }
    }
}
