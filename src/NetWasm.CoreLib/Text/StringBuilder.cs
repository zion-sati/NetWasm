// Portions derived from dotnet/runtime System.Private.CoreLib StringBuilder.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Text
{
    // A linked chunk representation follows the upstream StringBuilder model.
    // Unsupported serialization, reflection and runtime-intrinsic paths are
    // intentionally outside the NetWasm CoreLib profile.
    public sealed partial class StringBuilder
    {
        internal char[] m_ChunkChars;
        internal StringBuilder? m_ChunkPrevious;
        internal int m_ChunkLength;
        internal int m_ChunkOffset;
        internal int m_MaxCapacity;

        internal const int DefaultCapacity = 16;
        internal const int MaxChunkSize = 8000;

        public StringBuilder() : this(DefaultCapacity, int.MaxValue)
        {
        }

        public StringBuilder(int capacity) : this(capacity, int.MaxValue)
        {
        }

        public StringBuilder(string? value) : this(value, DefaultCapacity)
        {
        }

        public StringBuilder(string? value, int capacity) : this(
            value,
            0,
            value?.Length ?? 0,
            capacity)
        {
        }

        public StringBuilder(string? value, int startIndex, int length, int capacity)
        {
            ValidateNonNegative(startIndex, nameof(startIndex));
            ValidateNonNegative(length, nameof(length));
            ValidateNonNegative(capacity, nameof(capacity));

            value ??= string.Empty;
            if (startIndex > value.Length - length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            m_MaxCapacity = int.MaxValue;
            capacity = capacity == 0 ? DefaultCapacity : capacity;
            capacity = Math.Max(capacity, length);
            m_ChunkChars = new char[capacity];
            m_ChunkLength = length;
            for (var index = 0; index < length; index++)
            {
                m_ChunkChars[index] = value[startIndex + index];
            }
        }

        public StringBuilder(int capacity, int maxCapacity)
        {
            ValidateNonNegative(capacity, nameof(capacity));
            if (maxCapacity <= 0 || capacity > maxCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            }

            m_MaxCapacity = maxCapacity;
            m_ChunkChars = new char[capacity == 0 ? Math.Min(DefaultCapacity, maxCapacity) : capacity];
        }

        public int MaxCapacity
        {
            get { return m_MaxCapacity; }
        }

        public int Capacity
        {
            get => m_ChunkOffset + m_ChunkChars.Length;
            set
            {
                ValidateNonNegative(value, nameof(value));
                if (value > m_MaxCapacity || value < Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                if (value == Capacity)
                {
                    return;
                }

                var newLength = value - m_ChunkOffset;
                var replacement = new char[newLength];
                CopyChunk(m_ChunkChars, replacement, m_ChunkLength);
                m_ChunkChars = replacement;
            }
        }

        public int Length
        {
            get => m_ChunkOffset + m_ChunkLength;
            set
            {
                ValidateNonNegative(value, nameof(value));
                if (value > m_MaxCapacity)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                if (value == Length)
                {
                    return;
                }

                var oldCapacity = Capacity;
                var text = ToString();
                if (value < text.Length)
                {
                    ResetWithText(text, 0, value, oldCapacity);
                }
                else
                {
                    ResetWithText(text, 0, text.Length, Math.Max(oldCapacity, value));
                    Append('\0', value - text.Length);
                }
            }
        }

        public char this[int index]
        {
            get
            {
                var chunk = FindChunkForIndex(index);
                return chunk.m_ChunkChars[index - chunk.m_ChunkOffset];
            }
            set
            {
                var chunk = FindChunkForIndex(index);
                chunk.m_ChunkChars[index - chunk.m_ChunkOffset] = value;
            }
        }

        public int EnsureCapacity(int capacity)
        {
            ValidateNonNegative(capacity, nameof(capacity));
            if (capacity > m_MaxCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            if (Capacity < capacity)
            {
                Capacity = capacity;
            }
            return Capacity;
        }

        public override string ToString()
        {
            if (Length == 0)
            {
                return string.Empty;
            }

            var result = new char[Length];
            CopyTo(0, result, 0, result.Length);
            return string.Create(result);
        }

        public string ToString(int startIndex, int length)
        {
            ValidateRange(startIndex, length, Length);
            if (length == 0)
            {
                return string.Empty;
            }
            var result = new char[length];
            CopyTo(startIndex, result, 0, length);
            return string.Create(result);
        }

        public StringBuilder Clear()
        {
            m_ChunkPrevious = null;
            m_ChunkOffset = 0;
            m_ChunkLength = 0;
            return this;
        }

        public static StringBuilder MoveChunks(StringBuilder source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            var result = new StringBuilder(source.ToString(), source.Capacity);
            source.Clear();
            source.Capacity = 0;
            return result;
        }

        public ChunkEnumerator GetChunks() => new(this);

        public StringBuilder Append(string? value)
        {
            if (value is not null)
            {
                Append(value, 0, value.Length);
            }
            return this;
        }

        public StringBuilder Append(string? value, int startIndex, int count)
        {
            if (value is null)
            {
                if (startIndex == 0 && count == 0)
                {
                    return this;
                }
                throw new ArgumentNullException(nameof(value));
            }
            ValidateRange(startIndex, count, value.Length);
            AppendCharacters(value, startIndex, count);
            return this;
        }

        public StringBuilder Append(char[]? value)
        {
            if (value is not null)
            {
                Append(value, 0, value.Length);
            }
            return this;
        }

        public StringBuilder Append(char[]? value, int startIndex, int charCount)
        {
            if (value is null)
            {
                if (startIndex == 0 && charCount == 0)
                {
                    return this;
                }
                throw new ArgumentNullException(nameof(value));
            }
            ValidateRange(startIndex, charCount, value.Length);
            EnsureLength(charCount);
            var source = new char[charCount];
            for (var index = 0; index < charCount; index++)
            {
                source[index] = value[startIndex + index];
            }
            Append(string.Create(source));
            return this;
        }

        public StringBuilder Append(StringBuilder? value)
        {
            if (value is not null && value.Length != 0)
            {
                Append(value.ToString());
            }
            return this;
        }

        public StringBuilder Append(StringBuilder? value, int startIndex, int count)
        {
            if (value is null)
            {
                if (startIndex == 0 && count == 0)
                {
                    return this;
                }
                throw new ArgumentNullException(nameof(value));
            }
            ValidateRange(startIndex, count, value.Length);
            return Append(value.ToString(startIndex, count));
        }

        public StringBuilder Append(object? value) => value is null ? this : Append(value.ToString());

        public StringBuilder Append(char value)
        {
            EnsureLength(1);
            m_ChunkChars[m_ChunkLength++] = value;
            return this;
        }

        public StringBuilder Append(Rune value) => Append(value.ToString());

        public unsafe StringBuilder Append(char* value, int valueCount)
        {
            ValidateNonNegative(valueCount, nameof(valueCount));
            for (var index = 0; index < valueCount; index++)
            {
                Append(value[index]);
            }
            return this;
        }

        public StringBuilder Append(char value, int repeatCount)
        {
            ValidateNonNegative(repeatCount, nameof(repeatCount));
            EnsureLength(repeatCount);
            while (repeatCount-- > 0)
            {
                m_ChunkChars[m_ChunkLength++] = value;
                if (m_ChunkLength == m_ChunkChars.Length && repeatCount > 0)
                {
                    ExpandByABlock(repeatCount);
                }
            }
            return this;
        }

        public StringBuilder Append(bool value) => Append(value.ToString());
        public StringBuilder Append(sbyte value) => Append(value.ToString());
        public StringBuilder Append(byte value) => Append(value.ToString());
        public StringBuilder Append(short value) => Append(value.ToString());
        public StringBuilder Append(ushort value) => Append(value.ToString());
        public StringBuilder Append(int value) => Append(value.ToString());
        public StringBuilder Append(uint value) => Append(value.ToString());
        public StringBuilder Append(long value) => Append(value.ToString());
        public StringBuilder Append(ulong value) => Append(value.ToString());
        public StringBuilder Append(float value) => Append(value.ToString());
        public StringBuilder Append(double value) => Append(value.ToString());
        public StringBuilder Append(decimal value) => Append(value.ToString());

        public StringBuilder AppendLine() => Append(Environment.NewLine);

        public StringBuilder AppendLine(string? value)
        {
            Append(value);
            return AppendLine();
        }

        public StringBuilder Append(ReadOnlySpan<char> value) => Append(value.ToArray());

        public StringBuilder Append(ReadOnlyMemory<char> value) => Append(value.ToArray());

        public StringBuilder AppendJoin(char separator, params string?[] values) => AppendJoin(separator.ToString(), values);

        public StringBuilder AppendJoin(string? separator, params string?[] values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            return AppendJoin(separator, (IEnumerable<string?>)values);
        }

        public StringBuilder AppendJoin(char separator, params object?[] values) => AppendJoin(separator.ToString(), values);

        public StringBuilder AppendJoin(string? separator, params object?[] values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            return AppendJoin(separator, (IEnumerable<object?>)values);
        }

        public StringBuilder AppendJoin<T>(char separator, IEnumerable<T> values) => AppendJoin(separator.ToString(), values);

        public StringBuilder AppendJoin<T>(string? separator, IEnumerable<T> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            var separatorText = separator ?? string.Empty;
            var first = true;
            foreach (var value in values)
            {
                if (!first) Append(separatorText);
                first = false;
                Append(value is null ? null : value.ToString());
            }
            return this;
        }

        public StringBuilder AppendJoin(char separator, params ReadOnlySpan<string?> values) => AppendJoin(separator.ToString(), values.ToArray());
        public StringBuilder AppendJoin(string? separator, params ReadOnlySpan<string?> values) => AppendJoin(separator, values.ToArray());
        public StringBuilder AppendJoin(char separator, params ReadOnlySpan<object?> values) => AppendJoin(separator.ToString(), values.ToArray());
        public StringBuilder AppendJoin(string? separator, params ReadOnlySpan<object?> values) => AppendJoin(separator, values.ToArray());

        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            ValidateRange(sourceIndex, count, Length);
            ValidateRange(destinationIndex, count, destination.Length);
            for (var index = 0; index < count; index++)
            {
                destination[destinationIndex + index] = this[sourceIndex + index];
            }
        }

        public void CopyTo(int sourceIndex, Span<char> destination, int count)
        {
            ValidateRange(sourceIndex, count, Length);
            if (count > destination.Length) throw new ArgumentException();
            for (var index = 0; index < count; index++) destination[index] = this[sourceIndex + index];
        }

        public bool Equals(StringBuilder? other)
        {
            if (other is null || Length != other.Length) return false;
            for (var index = 0; index < Length; index++) if (this[index] != other[index]) return false;
            return true;
        }

        public bool Equals(ReadOnlySpan<char> other)
        {
            if (Length != other.Length) return false;
            for (var index = 0; index < Length; index++) if (this[index] != other[index]) return false;
            return true;
        }

        public StringBuilder Insert(int index, string? value)
        {
            if (value is null)
            {
                ValidateIndexForInsert(index);
                return this;
            }
            return Insert(index, value, 1);
        }

        public StringBuilder Insert(int index, string? value, int count)
        {
            ValidateIndexForInsert(index);
            ValidateNonNegative(count, nameof(count));
            if (value is null)
            {
                if (count == 0)
                {
                    return this;
                }
                throw new ArgumentNullException(nameof(value));
            }
            if (count == 0 || value.Length == 0)
            {
                return this;
            }
            var insertionLength = checked(value.Length * count);
            var original = ToString();
            var result = new char[checked(original.Length + insertionLength)];
            var destination = 0;
            CopyCharacters(original, 0, index, result, ref destination);
            for (var repeat = 0; repeat < count; repeat++)
            {
                CopyCharacters(value, 0, value.Length, result, ref destination);
            }
            CopyCharacters(original, index, original.Length - index, result, ref destination);
            ResetWithText(result, Math.Max(Capacity, result.Length));
            return this;
        }

        public StringBuilder Insert(int index, char value)
        {
            return Insert(index, value.ToString(), 1);
        }

        public StringBuilder Insert(int index, Rune value) => Insert(index, value.ToString(), 1);

        public StringBuilder Insert(int index, char[]? value)
        {
            return value is null ? Insert(index, (string?)null) : Insert(index, string.Create(value), 1);
        }

        public StringBuilder Insert(int index, char[]? value, int startIndex, int charCount)
        {
            if (value is null)
            {
                if (startIndex == 0 && charCount == 0)
                {
                    return Insert(index, (string?)null);
                }
                throw new ArgumentNullException(nameof(value));
            }
            ValidateRange(startIndex, charCount, value.Length);
            var text = new char[charCount];
            for (var offset = 0; offset < charCount; offset++)
            {
                text[offset] = value[startIndex + offset];
            }
            return Insert(index, string.Create(text), 1);
        }

        public StringBuilder Insert(int index, ReadOnlySpan<char> value) => Insert(index, string.Create(value.ToArray()), 1);

        public StringBuilder Insert(int index, object? value) => value is null ? Insert(index, (string?)null) : Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, bool value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, sbyte value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, byte value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, short value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, ushort value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, int value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, uint value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, long value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, ulong value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, float value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, double value) => Insert(index, value.ToString(), 1);
        public StringBuilder Insert(int index, decimal value) => Insert(index, value.ToString(), 1);

        public StringBuilder Remove(int startIndex, int length)
        {
            ValidateRange(startIndex, length, Length);
            if (length == 0)
            {
                return this;
            }
            var text = ToString();
            var result = new char[text.Length - length];
            var destination = 0;
            CopyCharacters(text, 0, startIndex, result, ref destination);
            CopyCharacters(text, startIndex + length, text.Length - startIndex - length, result, ref destination);
            ResetWithText(result, Capacity);
            return this;
        }

        public StringBuilder Replace(char oldChar, char newChar) => Replace(oldChar, newChar, 0, Length);

        public StringBuilder Replace(char oldChar, char newChar, int startIndex, int count)
        {
            ValidateRange(startIndex, count, Length);
            for (var index = startIndex; index < startIndex + count; index++)
            {
                if (this[index] == oldChar)
                {
                    this[index] = newChar;
                }
            }
            return this;
        }

        public StringBuilder Replace(Rune oldRune, Rune newRune) => Replace(oldRune, newRune, 0, Length);

        public StringBuilder Replace(Rune oldRune, Rune newRune, int startIndex, int count) =>
            Replace(oldRune.ToString(), newRune.ToString(), startIndex, count);

        public StringBuilder Replace(string oldValue, string? newValue) => Replace(oldValue, newValue, 0, Length);

        public StringBuilder Replace(string oldValue, string? newValue, int startIndex, int count)
        {
            if (oldValue is null)
            {
                throw new ArgumentNullException(nameof(oldValue));
            }
            if (oldValue.Length == 0)
            {
                throw new ArgumentException(nameof(oldValue));
            }
            ValidateRange(startIndex, count, Length);
            newValue ??= string.Empty;
            var source = ToString();
            var end = startIndex + count;
            var result = new StringBuilder(Math.Max(Capacity, Length));
            result.Append(source, 0, startIndex);
            var position = startIndex;
            while (position < end)
            {
                if (position <= end - oldValue.Length && MatchesAt(source, oldValue, position))
                {
                    result.Append(newValue);
                    position += oldValue.Length;
                }
                else
                {
                    result.Append(source[position++]);
                }
            }
            result.Append(source, end, source.Length - end);
            ResetWithText(result.ToString(), Math.Max(Capacity, result.Length));
            return this;
        }

        public StringBuilder Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue) =>
            Replace(string.Create(oldValue.ToArray()), string.Create(newValue.ToArray()));

        public StringBuilder Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue, int startIndex, int count) =>
            Replace(string.Create(oldValue.ToArray()), string.Create(newValue.ToArray()), startIndex, count);

        public StringBuilderRuneEnumerator EnumerateRunes() => new(this);

        public Rune GetRuneAt(int index)
        {
            if (TryGetRuneAt(index, out var value))
            {
                return value;
            }
            throw new ArgumentException(nameof(index));
        }

        public bool TryGetRuneAt(int index, out Rune value)
        {
            if ((uint)index >= (uint)Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var text = ToString();
            if (Rune.TryGetRuneAt(text, index, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        public StringBuilder AppendFormat(string format, object? arg0) => AppendFormat(null, format, arg0);
        public StringBuilder AppendFormat(string format, object? arg0, object? arg1) => AppendFormat(null, format, arg0, arg1);
        public StringBuilder AppendFormat(string format, object? arg0, object? arg1, object? arg2) => AppendFormat(null, format, arg0, arg1, arg2);
        public StringBuilder AppendFormat(string format, params object?[] args) => AppendFormat(null, format, args);

        public StringBuilder AppendFormat(IFormatProvider? provider, string format, object? arg0) => AppendFormat(provider, format, new object?[] { arg0 });
        public StringBuilder AppendFormat(IFormatProvider? provider, string format, object? arg0, object? arg1) => AppendFormat(provider, format, new object?[] { arg0, arg1 });
        public StringBuilder AppendFormat(IFormatProvider? provider, string format, object? arg0, object? arg1, object? arg2) => AppendFormat(provider, format, new object?[] { arg0, arg1, arg2 });

        public StringBuilder AppendFormat(IFormatProvider? provider, string format, params object?[] args)
        {
            if (format is null)
            {
                throw new ArgumentNullException(nameof(format));
            }
            if (args is null)
            {
                throw new ArgumentNullException(nameof(args));
            }
            return AppendFormat(provider, CompositeFormat.Parse(format), args);
        }

        public StringBuilder AppendFormat(IFormatProvider? provider, CompositeFormat format, params object?[] args)
        {
            if (format is null)
            {
                throw new ArgumentNullException(nameof(format));
            }
            if (args is null)
            {
                throw new ArgumentNullException(nameof(args));
            }
            format.ValidateNumberOfArgs(args.Length);
            foreach (var segment in format.Segments)
            {
                if (segment.Literal is not null)
                {
                    Append(segment.Literal);
                    continue;
                }

                var text = FormatValue(args[segment.ArgIndex], segment.Format, provider);
                var width = segment.Alignment < 0 ? -segment.Alignment : segment.Alignment;
                var padding = Math.Max(0, width - text.Length);
                if (segment.Alignment > 0)
                {
                    Append(' ', padding);
                }
                Append(text);
                if (segment.Alignment < 0)
                {
                    Append(' ', padding);
                }
            }
            return this;
        }

        public StringBuilder AppendFormat(string format, params ReadOnlySpan<object?> args) => AppendFormat(null, format, args.ToArray());
        public StringBuilder AppendFormat(IFormatProvider? provider, string format, params ReadOnlySpan<object?> args) => AppendFormat(provider, format, args.ToArray());
        public StringBuilder AppendFormat(IFormatProvider? provider, CompositeFormat format, params ReadOnlySpan<object?> args) => AppendFormat(provider, format, args.ToArray());
        public StringBuilder AppendFormat<TArg0>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0) => AppendFormat(provider, format, new object?[] { arg0 });
        public StringBuilder AppendFormat<TArg0, TArg1>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0, TArg1 arg1) => AppendFormat(provider, format, new object?[] { arg0, arg1 });
        public StringBuilder AppendFormat<TArg0, TArg1, TArg2>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0, TArg1 arg1, TArg2 arg2) => AppendFormat(provider, format, new object?[] { arg0, arg1, arg2 });

        public StringBuilder Append([InterpolatedStringHandlerArgument("")] ref AppendInterpolatedStringHandler handler) => this;

        public StringBuilder Append(IFormatProvider? provider, [InterpolatedStringHandlerArgument("", nameof(provider))] ref AppendInterpolatedStringHandler handler) => this;

        public StringBuilder AppendLine([InterpolatedStringHandlerArgument("")] ref AppendInterpolatedStringHandler handler) => AppendLine();

        public StringBuilder AppendLine(IFormatProvider? provider, [InterpolatedStringHandlerArgument("", nameof(provider))] ref AppendInterpolatedStringHandler handler) => AppendLine();

        internal static string FormatValue(object? value, string? format, IFormatProvider? provider)
        {
            if (provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter customFormatter)
            {
                return customFormatter.Format(format, value, provider) ?? string.Empty;
            }
            if (value is null)
            {
                return string.Empty;
            }
            if (value is IFormattable formattable)
            {
                return formattable.ToString(format, provider) ?? string.Empty;
            }
            if (value is int intValue) return intValue.ToString(format);
            if (value is uint uintValue) return uintValue.ToString(format);
            if (value is long longValue) return longValue.ToString(format);
            if (value is ulong ulongValue) return ulongValue.ToString(format);
            if (value is short shortValue) return shortValue.ToString(format);
            if (value is ushort ushortValue) return ushortValue.ToString(format);
            if (value is byte byteValue) return byteValue.ToString(format);
            if (value is sbyte sbyteValue) return sbyteValue.ToString(format);
            if (value is float floatValue) return floatValue.ToString(format);
            if (value is double doubleValue) return doubleValue.ToString(format);
            if (value is decimal decimalValue) return decimalValue.ToString();
            return value.ToString() ?? string.Empty;
        }

        private void AppendCharacters(string value, int startIndex, int count)
        {
            EnsureLength(count);
            while (count > 0)
            {
                var available = m_ChunkChars.Length - m_ChunkLength;
                if (available == 0)
                {
                    ExpandByABlock(count);
                    available = m_ChunkChars.Length;
                }
                var copy = Math.Min(available, count);
                for (var index = 0; index < copy; index++)
                {
                    m_ChunkChars[m_ChunkLength + index] = value[startIndex + index];
                }
                m_ChunkLength += copy;
                startIndex += copy;
                count -= copy;
            }
        }

        private void EnsureLength(int additional)
        {
            ValidateNonNegative(additional, nameof(additional));
            if (additional > m_MaxCapacity - Length)
            {
                throw new ArgumentOutOfRangeException(nameof(additional));
            }

            var required = Length + additional;
            if (required <= Capacity)
            {
                return;
            }

            var capacity = Capacity == 0 ? DefaultCapacity : Capacity;
            while (capacity < required && capacity < MaxChunkSize)
            {
                var doubled = capacity > MaxChunkSize / 2 ? MaxChunkSize : capacity * 2;
                capacity = Math.Max(doubled, capacity + 1);
            }
            if (capacity < required)
            {
                capacity = required;
            }
            Capacity = capacity;
        }

        private void ExpandByABlock(int minimum)
        {
            var newSize = Math.Max(minimum, Math.Min(Math.Max(Length, DefaultCapacity), MaxChunkSize));
            newSize = Math.Min(newSize, m_MaxCapacity - Length);
            if (newSize <= 0)
            {
                throw new OutOfMemoryException();
            }
            m_ChunkPrevious = new StringBuilder(this);
            m_ChunkOffset += m_ChunkLength;
            m_ChunkLength = 0;
            m_ChunkChars = new char[newSize];
        }

        private StringBuilder(StringBuilder source)
        {
            m_ChunkChars = source.m_ChunkChars;
            m_ChunkPrevious = source.m_ChunkPrevious;
            m_ChunkLength = source.m_ChunkLength;
            m_ChunkOffset = source.m_ChunkOffset;
            m_MaxCapacity = source.m_MaxCapacity;
        }

        private StringBuilder FindChunkForIndex(int index)
        {
            if ((uint)index >= (uint)Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            var chunk = this;
            while (chunk.m_ChunkOffset > index)
            {
                chunk = chunk.m_ChunkPrevious!;
            }
            return chunk;
        }

        private void ResetWithText(string text, int capacity) => ResetWithText(text, 0, text.Length, capacity);

        private void ResetWithText(string text, int startIndex, int length, int capacity)
        {
            var result = new char[length];
            for (var index = 0; index < length; index++)
            {
                result[index] = text[startIndex + index];
            }
            ResetWithText(result, Math.Max(capacity, length));
        }

        private void ResetWithText(char[] text, int capacity)
        {
            capacity = Math.Max(capacity, text.Length);
            if (capacity > m_MaxCapacity)
            {
                throw new OutOfMemoryException();
            }
            m_ChunkChars = new char[capacity];
            m_ChunkPrevious = null;
            m_ChunkOffset = 0;
            m_ChunkLength = text.Length;
            CopyChunk(text, m_ChunkChars, text.Length);
        }

        private static void CopyChunk(char[] source, char[] destination, int count)
        {
            for (var index = 0; index < count; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyCharacters(string source, int startIndex, int count, char[] destination, ref int destinationIndex)
        {
            for (var index = 0; index < count; index++)
            {
                destination[destinationIndex++] = source[startIndex + index];
            }
        }

        private static bool MatchesAt(string source, string value, int index)
        {
            for (var offset = 0; offset < value.Length; offset++)
            {
                if (source[index + offset] != value[offset])
                {
                    return false;
                }
            }
            return true;
        }

        private void ValidateIndexForInsert(int index)
        {
            if ((uint)index > (uint)Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateRange(int startIndex, int count, int length)
        {
            if (startIndex < 0 || count < 0 || startIndex > length - count)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        public struct ChunkEnumerator
        {
            private readonly StringBuilder[] _chunks;
            private int _index;

            internal ChunkEnumerator(StringBuilder builder)
            {
                var count = 0;
                var current = builder;
                while (current is not null)
                {
                    count++;
                    current = current.m_ChunkPrevious;
                }

                _chunks = new StringBuilder[count];
                current = builder;
                while (count > 0)
                {
                    _chunks[--count] = current!;
                    current = current!.m_ChunkPrevious;
                }
                _index = -1;
            }

            public ChunkEnumerator GetEnumerator() => this;
            public bool MoveNext() => ++_index < _chunks.Length;

            public ReadOnlyMemory<char> Current
            {
                get
                {
                    if ((uint)_index >= (uint)_chunks.Length)
                    {
                        throw new InvalidOperationException();
                    }
                    var chunk = _chunks[_index];
                    return new ReadOnlyMemory<char>(chunk.m_ChunkChars, 0, chunk.m_ChunkLength);
                }
            }
        }

        [InterpolatedStringHandler]
        public ref struct AppendInterpolatedStringHandler
        {
            private readonly StringBuilder _builder;
            private readonly IFormatProvider? _provider;

            public AppendInterpolatedStringHandler(int literalLength, int formattedCount, StringBuilder builder)
            {
                _builder = builder;
                _provider = null;
            }

            public AppendInterpolatedStringHandler(int literalLength, int formattedCount, StringBuilder builder, IFormatProvider? provider)
            {
                _builder = builder;
                _provider = provider;
            }

            public void AppendLiteral(string value) => _builder.Append(value);
            public void AppendFormatted<T>(T value) => AppendFormatted(value, 0, null);
            public void AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);
            public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);

            public void AppendFormatted<T>(T value, int alignment, string? format)
            {
                var text = FormatValue(value, format, _provider);
                AppendAligned(text, alignment);
            }

            public void AppendFormatted(string? value) => AppendFormatted<string?>(value);
            public void AppendFormatted(string? value, int alignment = 0, string? format = null) => AppendFormatted<string?>(value, alignment, format);
            public void AppendFormatted(object? value, int alignment = 0, string? format = null) => AppendFormatted<object?>(value, alignment, format);
            public void AppendFormatted(ReadOnlySpan<char> value) => AppendAligned(string.Create(value.ToArray()), 0);
            public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => AppendAligned(string.Create(value.ToArray()), alignment);

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
}
