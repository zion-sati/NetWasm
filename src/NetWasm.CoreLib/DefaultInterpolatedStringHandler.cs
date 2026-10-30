// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime DefaultInterpolatedStringHandler.cs at commit
// 474ed963699fc29809015392738a15231aaae2de. Enums format through their
// ISpanFormattable implementation, as Enum.TryFormatUnconstrained does.

using System.Buffers;

namespace System.Runtime.CompilerServices
{
    /// <summary>Provides the compiler's default interpolated string handler.</summary>
    [InterpolatedStringHandler]
    public ref struct DefaultInterpolatedStringHandler
    {
        private const int GuessedLengthPerHole = 11;
        private const int MinimumArrayPoolLength = 256;
        private const int MaximumLength = 0x3FFFFFDF;

        private readonly IFormatProvider? _provider;
        private char[]? _arrayToReturnToPool;
        private Span<char> _chars;
        private int _pos;
        private readonly bool _hasCustomFormatter;

        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount)
        {
            _provider = null;
            _chars = _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
            _pos = 0;
            _hasCustomFormatter = false;
        }

        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider)
        {
            _provider = provider;
            _chars = _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
            _pos = 0;
            _hasCustomFormatter = provider is not null && HasCustomFormatter(provider);
        }

        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider, Span<char> initialBuffer)
        {
            _provider = provider;
            _chars = initialBuffer;
            _arrayToReturnToPool = null;
            _pos = 0;
            _hasCustomFormatter = provider is not null && HasCustomFormatter(provider);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetDefaultLength(int literalLength, int formattedCount) =>
            Math.Max(MinimumArrayPoolLength, literalLength + (formattedCount * GuessedLengthPerHole));

        public override string ToString() => new string(Text);

        public string ToStringAndClear()
        {
            string result = new string(Text);
            Clear();
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            char[]? toReturn = _arrayToReturnToPool;

            // Defensive clear
            _arrayToReturnToPool = null;
            _chars = default;
            _pos = 0;

            if (toReturn is not null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }

        public ReadOnlySpan<char> Text => _chars.Slice(0, _pos);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendLiteral(string value)
        {
            if (value.AsSpan().TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                GrowThenCopyString(value);
            }
        }

        // Hole values are formatted according to the following policy:
        // 1. If an IFormatProvider was supplied and it provides an ICustomFormatter, use ICustomFormatter.Format (even if the value is null).
        // 2. If the type implements ISpanFormattable, use ISpanFormattable.TryFormat.
        // 3. If the type implements IFormattable, use IFormattable.ToString.
        // 4. Otherwise, use object.ToString.
        // For a value type, the type tests are constants and the TryFormat call
        // binds to the type's own method without boxing.
        public void AppendFormatted<T>(T value)
        {
            // If there's a custom formatter, always use it.
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format: null);
                return;
            }

            if (value is null)
            {
                return;
            }

            // A float or double hole writes its shortest round-trip form, as
            // ToString() does, so it does not reach the format string machinery.
            if (typeof(T) == typeof(float))
            {
                AppendShortestFloat((float)(object)value);
                return;
            }

            if (typeof(T) == typeof(double))
            {
                AppendShortestFloat((double)(object)value);
                return;
            }

            string? s;
            if (value is IFormattable)
            {
                // If the value can format itself directly into our buffer, do so.
                if (value is ISpanFormattable)
                {
                    int charsWritten;
                    while (!((ISpanFormattable)value).TryFormat(_chars.Slice(_pos), out charsWritten, default, _provider))
                    {
                        Grow();
                    }

                    _pos += charsWritten;
                    return;
                }

                s = ((IFormattable)value).ToString(format: null, _provider);
            }
            else
            {
                s = value.ToString();
            }

            if (s is not null)
            {
                AppendLiteral(s);
            }
        }

        private void AppendShortestFloat<TNumber>(TNumber value)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            int charsWritten;
            while (!Number.TryFormatShortestFloat(value, _chars.Slice(_pos), out charsWritten))
            {
                Grow();
            }

            _pos += charsWritten;
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            // If there's a custom formatter, always use it.
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format);
                return;
            }

            if (value is null)
            {
                return;
            }

            string? s;
            if (value is IFormattable)
            {
                // If the value can format itself directly into our buffer, do so.
                if (value is ISpanFormattable)
                {
                    int charsWritten;
                    while (!((ISpanFormattable)value).TryFormat(_chars.Slice(_pos), out charsWritten, format, _provider))
                    {
                        Grow();
                    }

                    _pos += charsWritten;
                    return;
                }

                s = ((IFormattable)value).ToString(format, _provider);
            }
            else
            {
                s = value.ToString();
            }

            if (s is not null)
            {
                AppendLiteral(s);
            }
        }

        public void AppendFormatted<T>(T value, int alignment)
        {
            int startingPos = _pos;
            AppendFormatted(value);
            if (alignment != 0)
            {
                AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
        }

        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            int startingPos = _pos;
            AppendFormatted(value, format);
            if (alignment != 0)
            {
                AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
        }

        public void AppendFormatted(scoped ReadOnlySpan<char> value)
        {
            // Fast path for when the value fits in the current buffer
            if (value.TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                GrowThenCopySpan(value);
            }
        }

        public void AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null)
        {
            bool leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            int paddingRequired = alignment - value.Length;
            if (paddingRequired <= 0)
            {
                // The value is as large or larger than the required amount of padding,
                // so just write the value.
                AppendFormatted(value);
                return;
            }

            // Write the value along with the appropriate padding.
            EnsureCapacityForAdditionalChars(value.Length + paddingRequired);
            if (leftAlign)
            {
                value.CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
                _chars.Slice(_pos, paddingRequired).Fill(' ');
                _pos += paddingRequired;
            }
            else
            {
                _chars.Slice(_pos, paddingRequired).Fill(' ');
                _pos += paddingRequired;
                value.CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
            }
        }

        public void AppendFormatted(string? value)
        {
            // Fast-path for no custom formatter and a non-null string that fits in the current destination buffer.
            if (!_hasCustomFormatter &&
                value is not null &&
                value.AsSpan().TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                AppendFormattedSlow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendFormattedSlow(string? value)
        {
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format: null);
            }
            else if (value is not null)
            {
                EnsureCapacityForAdditionalChars(value.Length);
                value.AsSpan().CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
            }
        }

        public void AppendFormatted(string? value, int alignment = 0, string? format = null) =>
            // Format is meaningless for strings and doesn't make sense for someone to specify.  We have the overload
            // simply to disambiguate between ROS<char> and object, just in case someone does specify a format, as
            // string is implicitly convertible to both. Just delegate to the T-based implementation.
            AppendFormatted<string?>(value, alignment, format);

        public void AppendFormatted(object? value, int alignment = 0, string? format = null) =>
            // This overload is expected to be used rarely, only if either a) something strongly typed as object is
            // formatted with both an alignment and a format, or b) the compiler is unable to target type to T. It
            // exists purely to help make cases from (b) compile. Just delegate to the T-based implementation.
            AppendFormatted<object?>(value, alignment, format);

        // The other interpolated string handlers format their holes here. A hole
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasCustomFormatter(IFormatProvider provider) =>
            provider.GetFormat(typeof(ICustomFormatter)) is not null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendCustomFormatter<T>(T value, string? format)
        {
            // This case is very rare, but we need to handle it prior to the other checks in case
            // a provider was used that supplied an ICustomFormatter which wanted to intercept the particular value.
            // We do the cast here rather than in the ctor, even though this could be executed multiple times per
            // formatting, to make the cast pay for play.
            ICustomFormatter? formatter = (ICustomFormatter?)_provider!.GetFormat(typeof(ICustomFormatter));

            if (formatter is not null && formatter.Format(format, value, _provider) is string customFormatted)
            {
                AppendLiteral(customFormatted);
            }
        }

        private void AppendOrInsertAlignmentIfNeeded(int startingPos, int alignment)
        {
            int charsWritten = _pos - startingPos;

            bool leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            int paddingNeeded = alignment - charsWritten;
            if (paddingNeeded > 0)
            {
                EnsureCapacityForAdditionalChars(paddingNeeded);

                if (leftAlign)
                {
                    _chars.Slice(_pos, paddingNeeded).Fill(' ');
                }
                else
                {
                    _chars.Slice(startingPos, charsWritten).CopyTo(_chars.Slice(startingPos + paddingNeeded));
                    _chars.Slice(startingPos, paddingNeeded).Fill(' ');
                }

                _pos += paddingNeeded;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacityForAdditionalChars(int additionalChars)
        {
            if (_chars.Length - _pos < additionalChars)
            {
                Grow(additionalChars);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowThenCopyString(string value)
        {
            Grow(value.Length);
            value.AsSpan().CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowThenCopySpan(scoped ReadOnlySpan<char> value)
        {
            Grow(value.Length);
            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
        private void Grow(int additionalChars)
        {
            // This method is called when the remaining space (_chars.Length - _pos) is
            // insufficient to store a specific number of additional characters.  Thus, we
            // need to grow to at least that new total. GrowCore will handle growing by more
            // than that if possible.
            GrowCore((uint)_pos + (uint)additionalChars);
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
        private void Grow()
        {
            // This method is called when the remaining space in _chars isn't sufficient to continue
            // the operation.  Thus, we need at least one character beyond _chars.Length.  GrowCore
            // will handle growing by more than that if possible.
            GrowCore((uint)_chars.Length + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // but reuse this grow logic directly in both of the above grow routines
        private void GrowCore(uint requiredMinCapacity)
        {
            // We want the max of how much space we actually required and doubling our capacity (without going beyond the max allowed length). We
            // also want to avoid asking for small arrays, to reduce the number of times we need to grow, and since we're working with unsigned
            // ints that could technically overflow if someone tried to, for example, append a huge string to a huge string, we also clamp to int.MaxValue.
            // Even if the array creation fails in such a case, we may later fail in ToStringAndClear.

            uint newCapacity = Math.Max(requiredMinCapacity, Math.Min((uint)_chars.Length * 2, MaximumLength));
            int arraySize = (int)Math.Clamp(newCapacity, MinimumArrayPoolLength, int.MaxValue);

            char[] newArray = ArrayPool<char>.Shared.Rent(arraySize);
            _chars.Slice(0, _pos).CopyTo(newArray);

            char[]? toReturn = _arrayToReturnToPool;
            _chars = _arrayToReturnToPool = newArray;

            if (toReturn is not null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }
    }
}
