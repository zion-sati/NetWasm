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
        // The characters written so far: the caller's initial buffer, or an
        // array that grows as needed. No StringBuilder, so interpolation costs
        // little more than the values it formats.
        private Span<char> _chars;
        private int _pos;

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
            _chars = new char[GetDefaultLength(literalLength, formattedCount)];
            _pos = 0;
        }

        public DefaultInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            IFormatProvider? provider,
            Span<char> initialBuffer)
        {
            _provider = provider;
            _chars = initialBuffer;
            _pos = 0;
        }

        internal static int GetDefaultLength(int literalLength, int formattedCount)
        {
            var additional = formattedCount > (int.MaxValue - literalLength) / 11
                ? int.MaxValue
                : literalLength + formattedCount * 11;
            return Math.Max(16, additional);
        }

        public override string ToString() => new string(_chars.Slice(0, _pos));

        public string ToStringAndClear()
        {
            var result = new string(_chars.Slice(0, _pos));
            Clear();
            return result;
        }

        public void Clear()
        {
            _chars = default;
            _pos = 0;
        }

        public ReadOnlySpan<char> Text => _chars.Slice(0, _pos);

        public void AppendLiteral(string value) => Append(value);

        public void AppendFormatted<T>(T value) => AppendAligned(FormatHole(value, _provider), 0);

        public void AppendFormatted<T>(T value, string? format) => AppendAligned(FormatHole(value, format, _provider), 0);

        public void AppendFormatted<T>(T value, int alignment) => AppendAligned(FormatHole(value, _provider), alignment);

        public void AppendFormatted<T>(T value, int alignment, string? format) =>
            AppendAligned(FormatHole(value, format, _provider), alignment);

        // Every interpolated string handler formats its holes here. A hole
        // without a format takes the primitive number's overload without one,
        // which writes what ToString(null, provider) writes but reaches no
        // format string parser or formatter: a program pays for those only when
        // it formats with a format string.
        internal static string FormatHole<T>(T value, IFormatProvider? provider)
        {
            if (TryFormatCustom(value, null, provider, out var custom)) return custom;
            if (typeof(T) == typeof(int)) return ((int)(object)value!).ToString(provider);
            if (typeof(T) == typeof(uint)) return ((uint)(object)value!).ToString(provider);
            if (typeof(T) == typeof(long)) return ((long)(object)value!).ToString(provider);
            if (typeof(T) == typeof(ulong)) return ((ulong)(object)value!).ToString(provider);
            if (typeof(T) == typeof(short)) return ((short)(object)value!).ToString(provider);
            if (typeof(T) == typeof(ushort)) return ((ushort)(object)value!).ToString(provider);
            if (typeof(T) == typeof(byte)) return ((byte)(object)value!).ToString(provider);
            if (typeof(T) == typeof(sbyte)) return ((sbyte)(object)value!).ToString(provider);
            if (typeof(T) == typeof(nint)) return ((nint)(object)value!).ToString(provider);
            if (typeof(T) == typeof(nuint)) return ((nuint)(object)value!).ToString(provider);
            if (typeof(T) == typeof(float)) return ((float)(object)value!).ToString(provider);
            if (typeof(T) == typeof(double)) return ((double)(object)value!).ToString(provider);
            if (typeof(T) == typeof(decimal)) return ((decimal)(object)value!).ToString(provider);
            return FormatHole(value, null, provider);
        }

        internal static string FormatHole<T>(T value, string? format, IFormatProvider? provider)
        {
            if (TryFormatCustom(value, format, provider, out var custom)) return custom;
            if (value is IFormattable)
            {
                return ((IFormattable)value).ToString(format, provider) ?? string.Empty;
            }
            return value?.ToString() ?? string.Empty;
        }

        // A provider's custom formatter formats every hole, as in .NET.
        private static bool TryFormatCustom<T>(T value, string? format, IFormatProvider? provider, out string text)
        {
            if (provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter formatter)
            {
                text = formatter.Format(format, value, provider) ?? string.Empty;
                return true;
            }
            text = string.Empty;
            return false;
        }

        public void AppendFormatted(scoped ReadOnlySpan<char> value) => Append(value);

        public void AppendFormatted(
            scoped ReadOnlySpan<char> value,
            int alignment = 0,
            string? format = null) => AppendAligned(value, alignment);

        // A provider's custom formatter also formats string holes, as in .NET.
        public void AppendFormatted(string? value)
        {
            if (_provider is null) Append(value);
            else AppendAligned(FormatHole(value, null, _provider), 0);
        }

        public void AppendFormatted(
            string? value,
            int alignment = 0,
            string? format = null) => AppendFormatted<string?>(value, alignment, format);

        public void AppendFormatted(
            object? value,
            int alignment = 0,
            string? format = null) => AppendFormatted<object?>(value, alignment, format);

        // A positive alignment pads on the left, a negative one on the right.
        private void AppendAligned(scoped ReadOnlySpan<char> text, int alignment)
        {
            var width = alignment < 0 ? -alignment : alignment;
            var padding = Math.Max(0, width - text.Length);
            if (alignment > 0) AppendPadding(padding);
            Append(text);
            if (alignment < 0) AppendPadding(padding);
        }

        private void Append(scoped ReadOnlySpan<char> value)
        {
            EnsureCapacity(value.Length);
            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        private void AppendPadding(int count)
        {
            EnsureCapacity(count);
            _chars.Slice(_pos, count).Fill(' ');
            _pos += count;
        }

        private void EnsureCapacity(int additional)
        {
            if (additional <= _chars.Length - _pos) return;
            var grown = new char[Math.Max(_pos + additional, Math.Max(16, _chars.Length * 2))];
            _chars.Slice(0, _pos).CopyTo(grown);
            _chars = grown;
        }
    }
}
