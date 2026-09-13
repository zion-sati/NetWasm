// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23); asynchronous members omitted.

using System.Text;

namespace System.IO
{
    public class StringWriter : TextWriter
    {
        private readonly StringBuilder _builder;
        private bool _isOpen;
        private Encoding? _encoding;

        public StringWriter() : this(new StringBuilder(), null) { }
        public StringWriter(IFormatProvider? formatProvider) : this(new StringBuilder(), formatProvider) { }
        public StringWriter(StringBuilder builder) : this(builder, null) { }

        public StringWriter(StringBuilder builder, IFormatProvider? formatProvider) : base(formatProvider)
        {
            ArgumentNullException.ThrowIfNull(builder);
            _builder = builder;
            _isOpen = true;
        }

        public override void Close() => Dispose(true);

        protected override void Dispose(bool disposing)
        {
            _isOpen = false;
            base.Dispose(disposing);
        }

        public override Encoding Encoding => _encoding ??= new UnicodeEncoding(false, false);

        public virtual StringBuilder GetStringBuilder() => _builder;

        public override void Write(char value)
        {
            ThrowIfClosed();
            _builder.Append(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            ThrowIfClosed();
            _builder.Append(buffer, index, count);
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            if (GetType() != typeof(StringWriter))
            {
                base.Write(buffer);
                return;
            }

            ThrowIfClosed();
            _builder.Append(buffer);
        }

        public override void Write(string? value)
        {
            ThrowIfClosed();
            _builder.Append(value);
        }

        public override void Write(StringBuilder? value)
        {
            if (GetType() != typeof(StringWriter))
            {
                base.Write(value);
                return;
            }

            ThrowIfClosed();
            _builder.Append(value);
        }

        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            if (GetType() != typeof(StringWriter))
            {
                base.WriteLine(buffer);
                return;
            }

            ThrowIfClosed();
            _builder.Append(buffer);
            WriteLine();
        }

        public override void WriteLine(StringBuilder? value)
        {
            if (GetType() != typeof(StringWriter))
            {
                base.WriteLine(value);
                return;
            }

            ThrowIfClosed();
            _builder.Append(value);
            WriteLine();
        }

        public override string ToString() => _builder.ToString();

        private void ThrowIfClosed()
        {
            if (!_isOpen)
            {
                throw new ObjectDisposedException(null, "Cannot write to a closed writer.");
            }
        }
    }
}
