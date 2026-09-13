// Portions derived from dotnet/runtime System.Private.CoreLib
// DefaultInterpolatedStringHandler.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.CompilerServices
{
    /// <summary>Provides the compiler's default interpolated string handler.</summary>
    [InterpolatedStringHandler]
    public ref struct DefaultInterpolatedStringHandler
    {
        private readonly IFormatProvider? _provider;
        private Text.StringBuilder _builder;

        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount)
            : this(literalLength, formattedCount, null)
        {
        }

        public DefaultInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            IFormatProvider? provider)
        {
            _provider = provider;
            _builder = new Text.StringBuilder(GetDefaultLength(literalLength, formattedCount));
        }

        public DefaultInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            IFormatProvider? provider,
            Span<char> initialBuffer)
        {
            _provider = provider;
            _builder = new Text.StringBuilder(Math.Max(initialBuffer.Length, GetDefaultLength(literalLength, formattedCount)));
        }

        internal static int GetDefaultLength(int literalLength, int formattedCount)
        {
            var additional = formattedCount > (int.MaxValue - literalLength) / 11
                ? int.MaxValue
                : literalLength + formattedCount * 11;
            return Math.Max(16, additional);
        }

        public override string ToString() => _builder.ToString();

        public string ToStringAndClear()
        {
            var result = _builder.ToString();
            _builder.Clear();
            return result;
        }

        public void Clear() => _builder.Clear();

        public ReadOnlySpan<char> Text
        {
            get { return new ReadOnlySpan<char>(_builder.ToString().ToCharArray()); }
        }

        public void AppendLiteral(string value) => _builder.Append(value);

        public void AppendFormatted<T>(T value) => AppendFormatted(value, 0, null);

        public void AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);

        public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);

        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            var text = System.Text.StringBuilder.FormatValue(value, format, _provider);
            AppendAligned(text, alignment);
        }

        public void AppendFormatted(scoped ReadOnlySpan<char> value)
        {
            _builder.Append(value);
        }

        public void AppendFormatted(
            scoped ReadOnlySpan<char> value,
            int alignment = 0,
            string? format = null)
        {
            var text = string.Create(value.ToArray());
            AppendAligned(text, alignment);
        }

        public void AppendFormatted(string? value) => _builder.Append(value);

        public void AppendFormatted(
            string? value,
            int alignment = 0,
            string? format = null) => AppendFormatted<string?>(value, alignment, format);

        public void AppendFormatted(
            object? value,
            int alignment = 0,
            string? format = null) => AppendFormatted<object?>(value, alignment, format);

        private void AppendAligned(string text, int alignment)
        {
            var width = alignment < 0 ? -alignment : alignment;
            var padding = Math.Max(0, width - text.Length);
            if (alignment > 0)
            {
                _builder.Append(' ', padding);
            }
            _builder.Append(text);
            if (alignment < 0)
            {
                _builder.Append(' ', padding);
            }
        }
    }
}
