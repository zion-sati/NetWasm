// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce).

using System.Text;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    public class StringWriter : TextWriter
    {
        private readonly StringBuilder _builder;
        private bool _isOpen;
        private Encoding? _encoding;
        private readonly bool _capturedInvariantCulture;

        // NetWasm: upstream passes CultureInfo.CurrentCulture, capturing the current culture when the writer is
        // created. The culture is captured from CurrentCultureMirror instead, which holds it unless it is the
        // invariant culture, so a writer created with the default provider does not make CultureInfo reachable
        // until its FormatProvider is read (by a numeric or formatted write, for example).
        public StringWriter() : this(new StringBuilder()) { }
        public StringWriter(IFormatProvider? formatProvider) : this(new StringBuilder(), formatProvider) { }
        public StringWriter(StringBuilder builder) : this(builder, CurrentCultureMirror.Culture, capturesCurrentCulture: true) { }

        public StringWriter(StringBuilder builder, IFormatProvider? formatProvider) : base(formatProvider)
        {
            ArgumentNullException.ThrowIfNull(builder);
            _builder = builder;
            _isOpen = true;
        }

        private StringWriter(StringBuilder builder, IFormatProvider? currentCulture, bool capturesCurrentCulture)
            : this(builder, currentCulture)
        {
            _capturedInvariantCulture = capturesCurrentCulture && currentCulture is null;
        }

        // NetWasm: a writer that captured the invariant culture reports it, as upstream's captured provider would.
        public override IFormatProvider FormatProvider =>
            _capturedInvariantCulture ? CultureInfo.InvariantCulture : base.FormatProvider;

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

        public override Task WriteAsync(char value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(string? value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            Write(buffer, index, count);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            Write(buffer.Span);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(StringBuilder? value, CancellationToken cancellationToken = default)
        {
            if (GetType() != typeof(StringWriter))
                return base.WriteAsync(value, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            ThrowIfClosed();
            _builder.Append(value);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(char value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(string? value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(StringBuilder? value, CancellationToken cancellationToken = default)
        {
            if (GetType() != typeof(StringWriter))
                return base.WriteLineAsync(value, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            ThrowIfClosed();
            _builder.Append(value);
            WriteLine();
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            WriteLine(buffer, index, count);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            WriteLine(buffer.Span);
            return Task.CompletedTask;
        }

        public override Task FlushAsync() => Task.CompletedTask;

        private void ThrowIfClosed()
        {
            if (!_isOpen)
            {
                throw new ObjectDisposedException(null, "Cannot write to a closed writer.");
            }
        }
    }
}
