// String is isolated from the remaining CoreLib types so its ordinal profile
// can evolve without turning the CoreLib surface into one monolithic file.
// Collection/enumeration members are adapted from dotnet/runtime
// System.Private.CoreLib String at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// The upstream implementation is licensed under MIT.
using System.Collections;
using System.Collections.Generic;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace System
{
    public sealed partial class String :
        IEnumerable<char>,
        IEnumerable,
        ICloneable,
        IComparable,
        IComparable<string?>,
        IConvertible,
        IEquatable<string?>,
        IParsable<string>,
        ISpanParsable<string>
    {
        public String(char value, int count)
        {
        }

        public static readonly string Empty = "";

        public int Length
        {
            get { return 0; }
        }

        public Text.StringRuneEnumerator EnumerateRunes() => new(this);

        public CharEnumerator GetEnumerator() => new(this);

        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (sourceIndex < 0 || count < 0 || sourceIndex > Length - count)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (destinationIndex < 0 || destinationIndex > destination.Length - count)
            {
                throw new ArgumentOutOfRangeException();
            }
            for (var index = 0; index < count; index++)
            {
                destination[destinationIndex + index] = this[sourceIndex + index];
            }
        }

        public void CopyTo(Span<char> destination)
        {
            if (Length > destination.Length)
            {
                throw new ArgumentException();
            }
            for (var index = 0; index < Length; index++)
            {
                destination[index] = this[index];
            }
        }

        public bool TryCopyTo(Span<char> destination)
        {
            if (Length > destination.Length)
            {
                return false;
            }
            CopyTo(destination);
            return true;
        }

        [Runtime.CompilerServices.IndexerName("Chars")]
        public char this[int index]
        {
            get { return default(char); }
        }

        [Runtime.CompilerServices.IndexerName("Chars")]
        public char this[Index index] => this[index.GetOffset(Length)];

        [Runtime.CompilerServices.IndexerName("Chars")]
        public string this[Range range]
        {
            get
            {
                var offsets = range.GetOffsetAndLength(Length);
                return Substring(offsets.Item1, offsets.Item2);
            }
        }

        public bool Equals(string? value)
        {
            if (value == null || Length != value.Length)
            {
                return false;
            }
            var equal = true;
            for (var index = 0; index < Length; index++)
            {
                if (equal && this[index] != value[index])
                {
                    equal = false;
                }
            }
            return equal;
        }

        public override bool Equals(object? value) =>
            value is string text && Equals(text);

        public override string ToString() => this;

        public static bool Equals(string? left, string? right) =>
            ReferenceEquals(left, right) || left != null && left.Equals(right);

        public static bool operator ==(string? left, string? right) => Equals(left, right);
        public static bool operator !=(string? left, string? right) => !Equals(left, right);

        public static int CompareOrdinal(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }
            if (left == null)
            {
                return -1;
            }
            if (right == null)
            {
                return 1;
            }
            var length = left.Length < right.Length ? left.Length : right.Length;
            var result = 0;
            for (var index = 0; index < length; index++)
            {
                if (result == 0)
                {
                    result = left[index] - right[index];
                }
            }
            if (result == 0 && left.Length != right.Length)
            {
                result = left.Length - right.Length;
            }
            return result;
        }

        public override int GetHashCode()
        {
            var hash = 2_166_136_261;
            for (var index = 0; index < Length; index++)
            {
                hash = (hash ^ this[index]) * 16_777_619;
            }
            hash ^= hash >> 16;
            hash *= 2_246_822_519;
            hash ^= hash >> 13;
            hash *= 3_266_489_917;
            hash ^= hash >> 16;
            return (int)hash;
        }

        public static bool IsNullOrEmpty(string? value) =>
            value == null || value.Length == 0;

        public static string Concat(string? left, string? right) =>
            Concat([left, right]);

        public static string Concat(string? first, string? second, string? third) =>
            Concat([first, second, third]);

        public static string Concat(
            string? first,
            string? second,
            string? third,
            string? fourth) => Concat([first, second, third, fourth]);

        public static string Concat(params string?[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException();
            }
            var length = 0;
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] != null)
                {
                    length = AddLength(length, values[index]!.Length);
                }
            }
            if (length == 0)
            {
                return Empty;
            }
            var result = new string('\0', length);
            var destination = 0;
            for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                var value = values[valueIndex];
                if (value == null)
                {
                    continue;
                }
                for (var index = 0; index < value.Length; index++)
                {
                    SetCharUnchecked(result, destination++, value[index]);
                }
            }
            return result;
        }

        public static string Concat(IEnumerable<string?> values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            var items = new Collections.Generic.List<string?>();
            foreach (var value in values)
            {
                items.Add(value);
            }
            return Concat(items.ToArray());
        }

        public static string Concat<T>(IEnumerable<T> values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            var items = new Collections.Generic.List<string?>();
            foreach (var value in values)
            {
                items.Add(value is null ? null : value.ToString());
            }
            return Concat(items.ToArray());
        }

        public static string Format(string format, object? arg0) =>
            Format(format, [arg0]);

        public static string Format(string format, object? arg0, object? arg1) =>
            Format(format, [arg0, arg1]);

        public static string Format(
            string format,
            object? arg0,
            object? arg1,
            object? arg2) => Format(format, [arg0, arg1, arg2]);

        public static string Format(string format, params object?[] args)
        {
            if (format == null || args == null)
            {
                throw new ArgumentNullException();
            }

            var builder = new Text.StringBuilder(format.Length + args.Length * 8);
            for (var position = 0; position < format.Length; position++)
            {
                var current = format[position];
                if (current == '{')
                {
                    if (position + 1 < format.Length && format[position + 1] == '{')
                    {
                        builder.Append('{');
                        position++;
                        continue;
                    }
                    position = AppendCompositeItem(builder, format, position + 1, args);
                    continue;
                }
                if (current == '}')
                {
                    if (position + 1 < format.Length && format[position + 1] == '}')
                    {
                        builder.Append('}');
                        position++;
                        continue;
                    }
                    throw new FormatException();
                }
                builder.Append(current);
            }
            return builder.ToString();
        }

        public string Substring(int startIndex) =>
            Substring(startIndex, Length - startIndex);

        public string Substring(int startIndex, int length)
        {
            ValidateRange(startIndex, length, Length);
            if (length == 0)
            {
                return Empty;
            }
            if (startIndex == 0 && length == Length)
            {
                return this;
            }
            var result = new string('\0', length);
            for (var index = 0; index < length; index++)
            {
                SetCharUnchecked(result, index, this[startIndex + index]);
            }
            return result;
        }

        public string Insert(int startIndex, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            if (startIndex < 0 || startIndex > Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return Concat(Substring(0, startIndex), value, Substring(startIndex));
        }

        public string Remove(int startIndex) => Remove(startIndex, Length - startIndex);

        public string Remove(int startIndex, int count)
        {
            ValidateRange(startIndex, count, Length);
            if (count == 0)
            {
                return this;
            }
            return Concat(Substring(0, startIndex), Substring(startIndex + count));
        }

        public string Replace(char oldChar, char newChar)
        {
            var result = new string('\0', Length);
            for (var index = 0; index < Length; index++)
            {
                var value = this[index];
                SetCharUnchecked(result, index, value == oldChar ? newChar : value);
            }
            return result;
        }

        public string Replace(string oldValue, string? newValue)
        {
            if (oldValue == null)
            {
                throw new ArgumentNullException();
            }
            if (oldValue.Length == 0)
            {
                throw new ArgumentException();
            }
            newValue ??= Empty;
            var matches = CountMatches(this, oldValue);
            if (matches == 0)
            {
                return this;
            }
            int resultLength;
            if (newValue.Length > oldValue.Length)
            {
                var growth = newValue.Length - oldValue.Length;
                if (matches > (int.MaxValue - Length) / growth)
                {
                    throw new OutOfMemoryException();
                }
                resultLength = Length + matches * growth;
            }
            else
            {
                resultLength = Length - matches * (oldValue.Length - newValue.Length);
            }
            return BuildReplacement(this, oldValue, newValue, resultLength);
        }

        private static int CountMatches(string source, string value)
        {
            var matches = 0;
            var search = 0;
            while (search <= source.Length - value.Length)
            {
                if (MatchesAt(source, value, search))
                {
                    matches++;
                    search += value.Length;
                }
                else
                {
                    search++;
                }
            }
            return matches;
        }

        internal static string Create(char[] characters)
        {
            if (characters == null)
            {
                throw new ArgumentNullException();
            }
            if (characters.Length == 0)
            {
                return Empty;
            }
            var result = new string('\0', characters.Length);
            for (var index = 0; index < characters.Length; index++)
            {
                SetCharUnchecked(result, index, characters[index]);
            }
            return result;
        }

        internal static string Create(char[] characters, int startIndex, int length)
        {
            if (characters is null)
            {
                throw new ArgumentNullException(nameof(characters));
            }
            if ((uint)startIndex > (uint)characters.Length ||
                (uint)length > (uint)(characters.Length - startIndex))
            {
                throw new ArgumentOutOfRangeException();
            }
            if (length == 0)
            {
                return Empty;
            }
            var result = new string('\0', length);
            for (var index = 0; index < length; index++)
            {
                SetCharUnchecked(result, index, characters[startIndex + index]);
            }
            return result;
        }

        private static string BuildReplacement(
            string original,
            string oldValue,
            string newValue,
            int resultLength)
        {
            var result = new string('\0', resultLength);
            var source = 0;
            var destination = 0;
            while (source < original.Length)
            {
                if (source <= original.Length - oldValue.Length &&
                    MatchesAt(original, oldValue, source))
                {
                    destination = Copy(newValue, result, destination);
                    source += oldValue.Length;
                }
                else
                {
                    SetCharUnchecked(result, destination++, original[source++]);
                }
            }
            return result;
        }

        public int IndexOf(char value) => IndexOf(value, 0, Length);
        public int IndexOf(char value, int startIndex) =>
            IndexOf(value, startIndex, Length - startIndex);

        public int IndexOf(char value, int startIndex, int count)
        {
            ValidateRange(startIndex, count, Length);
            var result = -1;
            for (var index = startIndex; index < startIndex + count; index++)
            {
                if (result < 0 && this[index] == value)
                {
                    result = index;
                }
            }
            return result;
        }

        public int LastIndexOf(char value)
        {
            for (var index = Length - 1; index >= 0; index--)
            {
                if (this[index] == value)
                {
                    return index;
                }
            }
            return -1;
        }

        public string Trim() => TrimWhiteSpace(trimStart: true, trimEnd: true);
        public string TrimStart() => TrimWhiteSpace(trimStart: true, trimEnd: false);
        public string TrimEnd() => TrimWhiteSpace(trimStart: false, trimEnd: true);

        public string Trim(char trimChar)
        {
            var start = 0;
            var end = Length - 1;
            while (start <= end && this[start] == trimChar)
            {
                start++;
            }
            while (end >= start && this[end] == trimChar)
            {
                end--;
            }
            return Substring(start, end - start + 1);
        }

        public string PadLeft(int totalWidth) => PadLeft(totalWidth, ' ');

        public string PadLeft(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            return totalWidth <= Length
                ? this
                : Concat(new string(paddingChar, totalWidth - Length), this);
        }

        public string PadRight(int totalWidth) => PadRight(totalWidth, ' ');

        public string PadRight(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            return totalWidth <= Length
                ? this
                : Concat(this, new string(paddingChar, totalWidth - Length));
        }

        public char[] ToCharArray()
        {
            var result = new char[Length];
            for (var index = 0; index < Length; index++)
            {
                result[index] = this[index];
            }
            return result;
        }

        public int IndexOf(string value, StringComparison comparisonType) =>
            IndexOf(value, 0, Length, comparisonType);

        public int IndexOf(string value, int startIndex, StringComparison comparisonType) =>
            IndexOf(value, startIndex, Length - startIndex, comparisonType);

        public int IndexOf(
            string value,
            int startIndex,
            int count,
            StringComparison comparisonType)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            ValidateOrdinal(comparisonType);
            ValidateRange(startIndex, count, Length);
            if (value.Length == 0)
            {
                return startIndex;
            }
            var last = startIndex + count - value.Length;
            var result = -1;
            for (var index = startIndex; index <= last; index++)
            {
                if (result < 0)
                {
                    var matched = 0;
                    while (matched < value.Length)
                    {
                        var current = comparisonType == StringComparison.OrdinalIgnoreCase
                            ? char.ToLowerInvariant(this[index + matched])
                            : this[index + matched];
                        var expected = comparisonType == StringComparison.OrdinalIgnoreCase
                            ? char.ToLowerInvariant(value[matched])
                            : value[matched];
                        if (current == expected)
                        {
                            matched++;
                        }
                        else
                        {
                            matched = value.Length + 1;
                        }
                    }
                    if (matched == value.Length)
                    {
                        result = index;
                    }
                }
            }
            return result;
        }

        public bool Contains(string value, StringComparison comparisonType) =>
            IndexOf(value, comparisonType) >= 0;

        public bool StartsWith(string value, StringComparison comparisonType)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            ValidateOrdinal(comparisonType);
            return value.Length <= Length &&
                IndexOf(value, 0, value.Length, comparisonType) == 0;
        }

        public bool EndsWith(string value, StringComparison comparisonType)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            ValidateOrdinal(comparisonType);
            return value.Length <= Length &&
                IndexOf(value, Length - value.Length, value.Length, comparisonType) ==
                    Length - value.Length;
        }

        public string[] Split(char separator)
        {
            var parts = 1;
            for (var index = 0; index < Length; index++)
            {
                if (this[index] == separator)
                {
                    parts++;
                }
            }
            var result = new string[parts];
            var start = 0;
            var part = 0;
            for (var index = 0; index <= Length; index++)
            {
                if (index == Length || this[index] == separator)
                {
                    result[part++] = Substring(start, index - start);
                    start = index + 1;
                }
            }
            return result;
        }

        public static string Join(string? separator, params string?[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException();
            }
            separator ??= Empty;
            var length = 0;
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] != null)
                {
                    length = AddLength(length, values[index]!.Length);
                }
                if (index != 0)
                {
                    length = AddLength(length, separator.Length);
                }
            }
            if (length == 0)
            {
                return Empty;
            }
            var result = new string('\0', length);
            var destination = 0;
            for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                if (valueIndex != 0)
                {
                    destination = Copy(separator, result, destination);
                }
                if (values[valueIndex] != null)
                {
                    destination = Copy(values[valueIndex]!, result, destination);
                }
            }
            return result;
        }

        public static string Join(string? separator, IEnumerable<string?> values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            var items = new Collections.Generic.List<string?>();
            foreach (var value in values)
            {
                items.Add(value);
            }
            return Join(separator, items.ToArray());
        }

        public static string Join<T>(char separator, IEnumerable<T> values) =>
            Join(separator.ToString(), values);

        public static string Join<T>(string? separator, IEnumerable<T> values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            var items = new Collections.Generic.List<string?>();
            foreach (var value in values)
            {
                items.Add(value is null ? null : value.ToString());
            }
            return Join(separator, items.ToArray());
        }

        private static int Copy(string source, string destination, int offset)
        {
            for (var index = 0; index < source.Length; index++)
            {
                SetCharUnchecked(destination, offset++, source[index]);
            }
            return offset;
        }

        private static int AppendCompositeItem(
            Text.StringBuilder builder,
            string format,
            int position,
            object?[] args)
        {
            SkipSpaces(format, ref position);
            if (position >= format.Length || !IsAsciiDigit(format[position]))
            {
                throw new FormatException();
            }
            var argumentIndex = 0;
            while (position < format.Length && IsAsciiDigit(format[position]))
            {
                var digit = format[position++] - '0';
                if (argumentIndex > (int.MaxValue - digit) / 10)
                {
                    throw new FormatException();
                }
                argumentIndex = argumentIndex * 10 + digit;
            }
            if ((uint)argumentIndex >= (uint)args.Length)
            {
                throw new FormatException();
            }

            SkipSpaces(format, ref position);
            var alignment = 0;
            if (position < format.Length && format[position] == ',')
            {
                position++;
                SkipSpaces(format, ref position);
                var leftAligned = position < format.Length && format[position] == '-';
                if (leftAligned)
                {
                    position++;
                }
                else if (position < format.Length && format[position] == '+')
                {
                    position++;
                }
                if (position >= format.Length || !IsAsciiDigit(format[position]))
                {
                    throw new FormatException();
                }
                while (position < format.Length && IsAsciiDigit(format[position]))
                {
                    var digit = format[position++] - '0';
                    if (alignment > (int.MaxValue - digit) / 10)
                    {
                        throw new FormatException();
                    }
                    alignment = alignment * 10 + digit;
                }
                if (leftAligned)
                {
                    alignment = -alignment;
                }
                SkipSpaces(format, ref position);
            }

            if (position < format.Length && format[position] == ':')
            {
                position++;
                if (position < format.Length &&
                    format[position] != 'G' && format[position] != 'g' &&
                    format[position] != '}')
                {
                    throw new FormatException();
                }
                if (position < format.Length && format[position] != '}')
                {
                    position++;
                }
            }
            if (position >= format.Length || format[position] != '}')
            {
                throw new FormatException();
            }

            var text = args[argumentIndex]?.ToString() ?? Empty;
            var width = alignment < 0 ? -alignment : alignment;
            var padding = width > text.Length ? width - text.Length : 0;
            if (alignment > 0)
            {
                builder.Append(' ', padding);
            }
            builder.Append(text);
            if (alignment < 0)
            {
                builder.Append(' ', padding);
            }
            return position;
        }

        private static void SkipSpaces(string format, ref int position)
        {
            while (position < format.Length && format[position] == ' ')
            {
                position++;
            }
        }

        private static bool IsAsciiDigit(char value) => value >= '0' && value <= '9';

        private string TrimWhiteSpace(bool trimStart, bool trimEnd)
        {
            var start = 0;
            var end = Length - 1;
            if (trimStart)
            {
                while (start <= end && IsWhiteSpace(this[start]))
                {
                    start++;
                }
            }
            if (trimEnd)
            {
                while (end >= start && IsWhiteSpace(this[end]))
                {
                    end--;
                }
            }
            return Substring(start, end - start + 1);
        }

        private static bool IsWhiteSpace(char value) => value is
            '\u0009' or '\u000a' or '\u000b' or '\u000c' or '\u000d' or
            '\u0020' or '\u0085' or '\u00a0' or '\u1680' or
            >= '\u2000' and <= '\u200a' or
            '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000';

        private static bool MatchesAt(string source, string value, int startIndex)
        {
            var matches = true;
            for (var index = 0; index < value.Length; index++)
            {
                if (source[startIndex + index] != value[index])
                {
                    matches = false;
                }
            }
            return matches;
        }

        private static void ValidateRange(int startIndex, int count, int length)
        {
            if (startIndex < 0 || count < 0 || startIndex > length - count)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static void ValidateOrdinal(StringComparison comparisonType)
        {
            if (comparisonType is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
            {
                throw new ArgumentException();
            }
        }

        private static int AddLength(int current, int additional)
        {
            if (additional > int.MaxValue - current)
            {
                throw new OutOfMemoryException();
            }
            return current + additional;
        }

        private static void SetCharUnchecked(string value, int index, char character)
        {
        }
    }

    // The source above contains the compact ordinal implementation used by the
    // compiler bootstrap. This companion keeps the remaining Microsoft String
    // contracts together, while deliberately omitting culture/normalization
    // overloads that are outside the invariant NetWasm profile.
    public sealed partial class String
    {
        public String(char[]? value)
        {
        }

        public String(char[] value, int startIndex, int length)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            ValidateRange(startIndex, length, value.Length);
        }

        public String(ReadOnlySpan<char> value)
        {
        }

        public unsafe String(char* value)
        {
            if (value is null)
            {
                return;
            }
            while (value[0] != '\0')
            {
                value++;
            }
        }

        public unsafe String(char* value, int startIndex, int length)
        {
            if (startIndex < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (value is null && length != 0)
            {
                throw new ArgumentNullException(nameof(value));
            }
        }

        public unsafe String(sbyte* value)
        {
            if (value is null)
            {
                return;
            }
            while (value[0] != 0)
            {
                value++;
            }
        }

        public unsafe String(sbyte* value, int startIndex, int length)
            : this(value, startIndex, length, null)
        {
        }

        public unsafe String(sbyte* value, int startIndex, int length, Encoding? encoding)
        {
            if (startIndex < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (value is null && length != 0)
            {
                throw new ArgumentNullException(nameof(value));
            }
            _ = encoding;
        }

        public static implicit operator ReadOnlySpan<char>(string? value) =>
            value is null ? default : new ReadOnlySpan<char>(value.ToCharArray());

        public object Clone() => this;

        public int CompareTo(object? value)
        {
            if (value is null)
            {
                return 1;
            }
            if (value is not string text)
            {
                throw new ArgumentException();
            }
            return CompareOrdinal(this, text);
        }

        public int CompareTo(string? strB) => CompareOrdinal(this, strB);

        public bool Contains(char value) => IndexOf(value) >= 0;

        public bool Contains(char value, StringComparison comparisonType) =>
            IndexOf(value, comparisonType) >= 0;

        public bool Contains(string value) => IndexOf(value) >= 0;

        public bool Contains(Rune value) => Contains(value.ToString());

        public bool Contains(Rune value, StringComparison comparisonType) =>
            Contains(value.ToString(), comparisonType);

        public bool EndsWith(char value) => Length != 0 && this[Length - 1] == value;

        public bool EndsWith(char value, StringComparison comparisonType) =>
            EndsWith(value, comparisonType, allowIgnoreCase: true);

        public bool EndsWith(string value) => EndsWith(value, StringComparison.Ordinal);

        public bool EndsWith(Rune value) => EndsWith(value.ToString(), StringComparison.Ordinal);

        public bool EndsWith(Rune value, StringComparison comparisonType) =>
            EndsWith(value.ToString(), comparisonType);

        public bool Equals(string? value, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            if (ReferenceEquals(this, value))
            {
                return true;
            }
            if (value is null || Length != value.Length)
            {
                return false;
            }
            return CompareOrdinal(this, value, comparisonType) == 0;
        }

        public int GetHashCode(StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            return comparisonType == StringComparison.Ordinal ? GetHashCode() : GetOrdinalIgnoreCaseHashCode(this);
        }

        private static char s_pinnableReference;
        private static List<string>? s_internedStrings;

        public ref readonly char GetPinnableReference() => ref s_pinnableReference;

        public TypeCode GetTypeCode() => TypeCode.String;

        public int IndexOf(char value, StringComparison comparisonType) =>
            IndexOf(value, 0, Length, comparisonType);

        public int IndexOf(char value, int startIndex, StringComparison comparisonType) =>
            IndexOf(value, startIndex, Length - startIndex, comparisonType);

        public int IndexOf(char value, int startIndex, int count, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            ValidateRange(startIndex, count, Length);
            var ignoreCase = comparisonType == StringComparison.OrdinalIgnoreCase;
            var expected = ignoreCase ? FoldOrdinalIgnoreCase(value) : value;
            for (var index = startIndex; index < startIndex + count; index++)
            {
                var current = ignoreCase ? FoldOrdinalIgnoreCase(this[index]) : this[index];
                if (current == expected)
                {
                    return index;
                }
            }
            return -1;
        }

        public int IndexOf(string value) => IndexOf(value, 0, Length, StringComparison.Ordinal);

        public int IndexOf(string value, int startIndex) =>
            IndexOf(value, startIndex, Length - startIndex, StringComparison.Ordinal);

        public int IndexOf(string value, int startIndex, int count) =>
            IndexOf(value, startIndex, count, StringComparison.Ordinal);

        public int IndexOf(Rune value) => IndexOf(value, 0, Length, StringComparison.Ordinal);

        public int IndexOf(Rune value, int startIndex) =>
            IndexOf(value, startIndex, Length - startIndex, StringComparison.Ordinal);

        public int IndexOf(Rune value, int startIndex, int count) =>
            IndexOf(value, startIndex, count, StringComparison.Ordinal);

        public int IndexOf(Rune value, StringComparison comparisonType) =>
            IndexOf(value, 0, Length, comparisonType);

        public int IndexOf(Rune value, int startIndex, StringComparison comparisonType) =>
            IndexOf(value, startIndex, Length - startIndex, comparisonType);

        public int IndexOf(Rune value, int startIndex, int count, StringComparison comparisonType) =>
            IndexOf(value.ToString(), startIndex, count, comparisonType);

        public int IndexOfAny(char[] anyOf) => IndexOfAny(anyOf, 0, Length);

        public int IndexOfAny(char[] anyOf, int startIndex) =>
            IndexOfAny(anyOf, startIndex, Length - startIndex);

        public int IndexOfAny(char[] anyOf, int startIndex, int count)
        {
            if (anyOf is null)
            {
                throw new ArgumentNullException(nameof(anyOf));
            }
            ValidateRange(startIndex, count, Length);
            for (var index = startIndex; index < startIndex + count; index++)
            {
                for (var separatorIndex = 0; separatorIndex < anyOf.Length; separatorIndex++)
                {
                    if (this[index] == anyOf[separatorIndex])
                    {
                        return index;
                    }
                }
            }
            return -1;
        }

        public int LastIndexOf(char value, int startIndex) =>
            LastIndexOf(value, startIndex, startIndex + 1, StringComparison.Ordinal);

        public int LastIndexOf(char value, int startIndex, int count) =>
            LastIndexOf(value, startIndex, count, StringComparison.Ordinal);

        public int LastIndexOf(char value, StringComparison comparisonType) =>
            Length == 0 ? -1 : LastIndexOf(value, Length - 1, Length, comparisonType);

        public int LastIndexOf(char value, int startIndex, StringComparison comparisonType) =>
            LastIndexOf(value, startIndex, startIndex + 1, comparisonType);

        public int LastIndexOf(char value, int startIndex, int count, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            ValidateLastRange(startIndex, count);
            var ignoreCase = comparisonType == StringComparison.OrdinalIgnoreCase;
            var expected = ignoreCase ? FoldOrdinalIgnoreCase(value) : value;
            for (var index = startIndex; index >= startIndex - count + 1; index--)
            {
                var current = ignoreCase ? FoldOrdinalIgnoreCase(this[index]) : this[index];
                if (current == expected)
                {
                    return index;
                }
            }
            return -1;
        }

        public int LastIndexOf(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return value.Length == 0
                ? Length
                : LastIndexOf(value, Length == 0 ? 0 : Length - 1, Length, StringComparison.Ordinal);
        }

        public int LastIndexOf(string value, int startIndex) =>
            LastIndexOf(value, startIndex, startIndex + 1, StringComparison.Ordinal);

        public int LastIndexOf(string value, int startIndex, int count) =>
            LastIndexOf(value, startIndex, count, StringComparison.Ordinal);

        public int LastIndexOf(string value, StringComparison comparisonType)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            ValidateComparison(comparisonType);
            return value.Length == 0
                ? Length
                : LastIndexOf(value, Length == 0 ? 0 : Length - 1, Length, comparisonType);
        }

        public int LastIndexOf(string value, int startIndex, StringComparison comparisonType) =>
            LastIndexOf(value, startIndex, startIndex + 1, comparisonType);

        public int LastIndexOf(string value, int startIndex, int count, StringComparison comparisonType)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            ValidateComparison(comparisonType);
            ValidateLastRange(startIndex, count);
            if (value.Length == 0)
            {
                return startIndex;
            }
            for (var index = startIndex; index >= startIndex - count + 1; index--)
            {
                if (index + value.Length > Length)
                {
                    continue;
                }
                if (MatchesAt(this, value, index, comparisonType))
                {
                    return index;
                }
            }
            return -1;
        }

        public int LastIndexOf(Rune value) => LastIndexOf(value.ToString());

        public int LastIndexOf(Rune value, int startIndex) => LastIndexOf(value.ToString(), startIndex);

        public int LastIndexOf(Rune value, int startIndex, int count) =>
            LastIndexOf(value.ToString(), startIndex, count);

        public int LastIndexOf(Rune value, StringComparison comparisonType) =>
            LastIndexOf(value.ToString(), comparisonType);

        public int LastIndexOf(Rune value, int startIndex, StringComparison comparisonType) =>
            LastIndexOf(value.ToString(), startIndex, comparisonType);

        public int LastIndexOf(Rune value, int startIndex, int count, StringComparison comparisonType) =>
            LastIndexOf(value.ToString(), startIndex, count, comparisonType);

        public int LastIndexOfAny(char[] anyOf) =>
            LastIndexOfAny(anyOf, Length == 0 ? 0 : Length - 1, Length);

        public int LastIndexOfAny(char[] anyOf, int startIndex) =>
            LastIndexOfAny(anyOf, startIndex, startIndex + 1);

        public int LastIndexOfAny(char[] anyOf, int startIndex, int count)
        {
            if (anyOf is null)
            {
                throw new ArgumentNullException(nameof(anyOf));
            }
            ValidateLastRange(startIndex, count);
            for (var index = startIndex; index >= startIndex - count + 1; index--)
            {
                for (var separatorIndex = 0; separatorIndex < anyOf.Length; separatorIndex++)
                {
                    if (this[index] == anyOf[separatorIndex])
                    {
                        return index;
                    }
                }
            }
            return -1;
        }

        public string Replace(string oldValue, string? newValue, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            if (comparisonType == StringComparison.Ordinal)
            {
                return Replace(oldValue, newValue);
            }
            if (oldValue is null)
            {
                throw new ArgumentNullException(nameof(oldValue));
            }
            if (oldValue.Length == 0)
            {
                throw new ArgumentException();
            }
            newValue ??= Empty;
            var builder = new StringBuilder(Length);
            var source = 0;
            while (source < Length)
            {
                if (source <= Length - oldValue.Length && MatchesAt(this, oldValue, source, comparisonType))
                {
                    builder.Append(newValue);
                    source += oldValue.Length;
                }
                else
                {
                    builder.Append(this[source++]);
                }
            }
            return builder.ToString();
        }

        public string Replace(Rune oldRune, Rune newRune) => Replace(oldRune.ToString(), newRune.ToString());

        public string ReplaceLineEndings() => ReplaceLineEndings("\n");

        public string ReplaceLineEndings(string replacementText)
        {
            if (replacementText is null)
            {
                throw new ArgumentNullException(nameof(replacementText));
            }
            var builder = new StringBuilder(Length);
            for (var index = 0; index < Length; index++)
            {
                var current = this[index];
                if (current == '\r')
                {
                    if (index + 1 < Length && this[index + 1] == '\n')
                    {
                        index++;
                    }
                    builder.Append(replacementText);
                }
                else if (current == '\n' || current == '\u0085' || current == '\u2028' || current == '\u2029')
                {
                    builder.Append(replacementText);
                }
                else
                {
                    builder.Append(current);
                }
            }
            return builder.ToString();
        }

        public string[] Split(char separator, StringSplitOptions options = System.StringSplitOptions.None) =>
            Split(separator, -1, options);

        public string[] Split(char separator, int count, StringSplitOptions options = System.StringSplitOptions.None) =>
            SplitCore(new[] { separator.ToString() }, count, options);

        public string[] Split(char[]? separator, int count) =>
            Split(separator, count, StringSplitOptions.None);

        public string[] Split(char[]? separator, int count, StringSplitOptions options) =>
            SplitCore(ToStringSeparators(separator), count, options);

        public string[] Split(char[]? separator, StringSplitOptions options) =>
            Split(separator, -1, options);

        public string[] Split(params char[]? separator) => Split(separator, -1, StringSplitOptions.None);

        public string[] Split(params ReadOnlySpan<char> separator) =>
            Split(separator.ToArray(), -1, StringSplitOptions.None);

        public string[] Split(string? separator, StringSplitOptions options = System.StringSplitOptions.None) =>
            Split(separator, -1, options);

        public string[] Split(string? separator, int count, StringSplitOptions options = System.StringSplitOptions.None) =>
            SplitCore(separator is null ? null : new[] { separator }, count, options);

        public string[] Split(string[]? separator, StringSplitOptions options) =>
            Split(separator, -1, options);

        public string[] Split(string[]? separator, int count, StringSplitOptions options) =>
            SplitCore(separator, count, options);

        public string[] Split(Rune separator, StringSplitOptions options = System.StringSplitOptions.None) =>
            Split(separator, -1, options);

        public string[] Split(Rune separator, int count, StringSplitOptions options = System.StringSplitOptions.None) =>
            SplitCore(new[] { separator.ToString() }, count, options);

        public bool StartsWith(char value) => Length != 0 && this[0] == value;

        public bool StartsWith(char value, StringComparison comparisonType) =>
            StartsWithChar(value, comparisonType);

        public bool StartsWith(string value) => StartsWith(value, StringComparison.Ordinal);

        public bool StartsWith(Rune value) => StartsWith(value.ToString(), StringComparison.Ordinal);

        public bool StartsWith(Rune value, StringComparison comparisonType) =>
            StartsWith(value.ToString(), comparisonType);

        public char[] ToCharArray(int startIndex, int length)
        {
            ValidateRange(startIndex, length, Length);
            var result = new char[length];
            for (var index = 0; index < length; index++)
            {
                result[index] = this[startIndex + index];
            }
            return result;
        }

        public string ToLower() => ToLowerInvariant();

        public string ToLowerInvariant() => ToLowerOrdinal();

        public bool IsNormalized() => throw new PlatformNotSupportedException();

        public string Normalize() => throw new PlatformNotSupportedException();

        public string ToLowerOrdinal() => MapInvariant(this, upper: false);

        public string ToString(IFormatProvider? provider) => this;

        public string ToUpper() => ToUpperInvariant();

        public string ToUpperInvariant() => ToUpperOrdinal();

        public string ToUpperOrdinal() => MapInvariant(this, upper: true);

        public string Trim(Rune trimRune) => Trim(trimRune.ToString().ToCharArray());

        public string Trim(params char[]? trimChars) => TrimCore(trimChars, trimStart: true, trimEnd: true);

        public string TrimEnd(char trimChar) => TrimEnd(new[] { trimChar });

        public string TrimEnd(Rune trimRune) => TrimEnd(trimRune.ToString().ToCharArray());

        public string TrimEnd(params char[]? trimChars) => TrimCore(trimChars, trimStart: false, trimEnd: true);

        public string TrimStart(char trimChar) => TrimStart(new[] { trimChar });

        public string TrimStart(Rune trimRune) => TrimStart(trimRune.ToString().ToCharArray());

        public string TrimStart(params char[]? trimChars) => TrimCore(trimChars, trimStart: true, trimEnd: false);

        public static int Compare(string? strA, string? strB) =>
            Compare(strA, strB, StringComparison.Ordinal);

        public static int Compare(string? strA, string? strB, bool ignoreCase) =>
            Compare(strA, strB, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        public static int Compare(string? strA, string? strB, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            return CompareOrdinal(strA, strB, comparisonType);
        }

        public static int Compare(
            string? strA,
            int indexA,
            string? strB,
            int indexB,
            int length) =>
            Compare(strA, indexA, strB, indexB, length, StringComparison.Ordinal);

        public static int Compare(
            string? strA,
            int indexA,
            string? strB,
            int indexB,
            int length,
            bool ignoreCase) =>
            Compare(strA, indexA, strB, indexB, length,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        public static int Compare(
            string? strA,
            int indexA,
            string? strB,
            int indexB,
            int length,
            StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            var left = SliceForCompare(strA, indexA, length);
            var right = SliceForCompare(strB, indexB, length);
            return CompareOrdinal(left, right, comparisonType);
        }

        public static int CompareOrdinal(
            string? strA,
            int indexA,
            string? strB,
            int indexB,
            int length)
        {
            var left = SliceForCompare(strA, indexA, length);
            var right = SliceForCompare(strB, indexB, length);
            return CompareOrdinal(left, right);
        }

        public static string Concat(object? arg0) => arg0?.ToString() ?? Empty;

        public static string Concat(object? arg0, object? arg1) =>
            Concat(arg0?.ToString(), arg1?.ToString());

        public static string Concat(object? arg0, object? arg1, object? arg2) =>
            Concat(arg0?.ToString(), arg1?.ToString(), arg2?.ToString());

        public static string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1) =>
            Concat(Create(str0.ToArray()), Create(str1.ToArray()));

        public static string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1, ReadOnlySpan<char> str2) =>
            Concat(Concat(str0, str1), Create(str2.ToArray()));

        public static string Concat(ReadOnlySpan<char> str0, ReadOnlySpan<char> str1, ReadOnlySpan<char> str2, ReadOnlySpan<char> str3) =>
            Concat(Concat(str0, str1, str2), Create(str3.ToArray()));

        public static string Concat(params object?[] args)
        {
            if (args is null)
            {
                throw new ArgumentNullException(nameof(args));
            }
            var values = new string?[args.Length];
            for (var index = 0; index < args.Length; index++)
            {
                values[index] = args[index]?.ToString();
            }
            return Concat(values);
        }

        public static string Concat(params ReadOnlySpan<object?> args)
        {
            var values = new string?[args.Length];
            for (var index = 0; index < args.Length; index++)
            {
                values[index] = args[index]?.ToString();
            }
            return Concat(values);
        }

        public static string Concat(params ReadOnlySpan<string?> values) => Concat(values.ToArray());

        public static string Copy(string str)
        {
            if (str is null)
            {
                throw new ArgumentNullException(nameof(str));
            }
            return str.Length == 0 ? Empty : str.Substring(0, str.Length);
        }

        public static string Create<TState>(int length, TState state, SpanAction<char, TState> action)
            where TState : allows ref struct
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            if (length == 0)
            {
                return Empty;
            }
            var chars = new char[length];
            action(new Span<char>(chars), state);
            return Create(chars);
        }

        public static string Create(IFormatProvider? provider, ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        public static string Create(IFormatProvider? provider, Span<char> initialBuffer, ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        public static bool Equals(string? a, string? b, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            return a is not null && b is not null && CompareOrdinal(a, b, comparisonType) == 0;
        }

        public static string Format(IFormatProvider? provider, string format, object? arg0) =>
            Format(format, arg0);

        public static string Format(IFormatProvider? provider, string format, object? arg0, object? arg1) =>
            Format(format, arg0, arg1);

        public static string Format(IFormatProvider? provider, string format, object? arg0, object? arg1, object? arg2) =>
            Format(format, arg0, arg1, arg2);

        public static string Format(IFormatProvider? provider, string format, params object?[] args) =>
            Format(format, args);

        public static string Format(IFormatProvider? provider, string format, params ReadOnlySpan<object?> args) =>
            Format(format, args.ToArray());

        public static string Format(IFormatProvider? provider, CompositeFormat format, params object?[] args) =>
            Format(provider, format, new ReadOnlySpan<object?>(args));

        public static string Format(IFormatProvider? provider, CompositeFormat format, params ReadOnlySpan<object?> args) =>
            Format(provider, format.Format, args);

        public static string Format(string format, params ReadOnlySpan<object?> args) =>
            Format(format, args.ToArray());

        public static string Format<TArg0>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0) =>
            Format(provider, format.Format, arg0);

        public static string Format<TArg0, TArg1>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0, TArg1 arg1) =>
            Format(provider, format.Format, arg0, arg1);

        public static string Format<TArg0, TArg1, TArg2>(IFormatProvider? provider, CompositeFormat format, TArg0 arg0, TArg1 arg1, TArg2 arg2) =>
            Format(provider, format.Format, arg0, arg1, arg2);

        public static int GetHashCode(ReadOnlySpan<char> value) => Create(value.ToArray()).GetHashCode();

        public static int GetHashCode(ReadOnlySpan<char> value, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            var text = Create(value.ToArray());
            return comparisonType == StringComparison.Ordinal ? text.GetHashCode() : GetOrdinalIgnoreCaseHashCode(text);
        }

        public static string Intern(string str)
        {
            if (str is null)
            {
                throw new ArgumentNullException(nameof(str));
            }
            var internedStrings = s_internedStrings ??= new List<string>();
            for (var index = 0; index < internedStrings.Count; index++)
            {
                if (Equals(internedStrings[index], str))
                {
                    return internedStrings[index];
                }
            }
            internedStrings.Add(str);
            return str;
        }

        public static string? IsInterned(string str)
        {
            if (str is null)
            {
                throw new ArgumentNullException(nameof(str));
            }
            var internedStrings = s_internedStrings;
            if (internedStrings is null)
            {
                return null;
            }
            for (var index = 0; index < internedStrings.Count; index++)
            {
                if (Equals(internedStrings[index], str))
                {
                    return internedStrings[index];
                }
            }
            return null;
        }

        public static bool IsNullOrWhiteSpace(string? value)
        {
            if (value is null)
            {
                return true;
            }
            for (var index = 0; index < value.Length; index++)
            {
                if (!IsWhiteSpace(value[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public static string Join(char separator, string?[] value, int startIndex, int count) =>
            Join(separator.ToString(), value, startIndex, count);

        public static string Join(char separator, params object?[] values) =>
            Join(separator.ToString(), values);

        public static string Join(char separator, params ReadOnlySpan<object?> values) =>
            Join(separator.ToString(), values.ToArray());

        public static string Join(char separator, params ReadOnlySpan<string?> value) =>
            Join(separator.ToString(), value.ToArray());

        public static string Join(char separator, params string?[] value) =>
            Join(separator.ToString(), value);

        public static string Join(string? separator, string?[] value, int startIndex, int count)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            ValidateRange(startIndex, count, value.Length);
            var slice = new string?[count];
            for (var index = 0; index < count; index++)
            {
                slice[index] = value[startIndex + index];
            }
            return Join(separator, slice);
        }

        public static string Join(string? separator, params object?[] values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            var strings = new string?[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                strings[index] = values[index]?.ToString();
            }
            return Join(separator, strings);
        }

        public static string Join(string? separator, params ReadOnlySpan<object?> values) =>
            Join(separator, values.ToArray());

        public static string Join(string? separator, params ReadOnlySpan<string?> value) =>
            Join(separator, value.ToArray());

        private bool EndsWith(char value, StringComparison comparisonType, bool allowIgnoreCase)
        {
            ValidateComparison(comparisonType);
            return Length != 0 && (!allowIgnoreCase || comparisonType == StringComparison.Ordinal || comparisonType == StringComparison.OrdinalIgnoreCase) &&
                (comparisonType == StringComparison.Ordinal ? this[Length - 1] == value : FoldOrdinalIgnoreCase(this[Length - 1]) == FoldOrdinalIgnoreCase(value));
        }

        private static void ValidateComparison(StringComparison comparisonType)
        {
            if (comparisonType is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
            {
                throw new ArgumentException(nameof(comparisonType));
            }
        }

        private bool StartsWithChar(char value, StringComparison comparisonType)
        {
            ValidateComparison(comparisonType);
            return Length != 0 && (comparisonType == StringComparison.Ordinal
                ? this[0] == value
                : FoldOrdinalIgnoreCase(this[0]) == FoldOrdinalIgnoreCase(value));
        }

        private void ValidateLastRange(int startIndex, int count)
        {
            if (Length == 0)
            {
                if (startIndex != 0 && startIndex != -1 || count < 0)
                {
                    throw new ArgumentOutOfRangeException();
                }
                return;
            }
            if (startIndex < 0 || startIndex >= Length || count < 0 || count > startIndex + 1)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static int CompareOrdinal(string? left, string? right, StringComparison comparisonType)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }
            if (left is null)
            {
                return -1;
            }
            if (right is null)
            {
                return 1;
            }
            var length = left.Length < right.Length ? left.Length : right.Length;
            for (var index = 0; index < length; index++)
            {
                var leftChar = comparisonType == StringComparison.OrdinalIgnoreCase ? FoldOrdinalIgnoreCase(left[index]) : left[index];
                var rightChar = comparisonType == StringComparison.OrdinalIgnoreCase ? FoldOrdinalIgnoreCase(right[index]) : right[index];
                if (leftChar != rightChar)
                {
                    return leftChar - rightChar;
                }
            }
            return left.Length - right.Length;
        }

        private static string? SliceForCompare(string? value, int index, int length)
        {
            if (value is null)
            {
                if (index != 0 || length != 0)
                {
                    throw new ArgumentOutOfRangeException();
                }
                return null;
            }
            ValidateRange(index, length, value.Length);
            return value.Substring(index, length);
        }

        private static bool MatchesAt(string source, string value, int startIndex, StringComparison comparisonType)
        {
            if (startIndex < 0 || startIndex + value.Length > source.Length)
            {
                return false;
            }
            return CompareOrdinal(source.Substring(startIndex, value.Length), value, comparisonType) == 0;
        }

        private static char FoldOrdinalIgnoreCase(char value) => char.ToUpperInvariant(value);

        private static string MapInvariant(string value, bool upper)
        {
            var result = new char[value.Length];
            for (var index = 0; index < value.Length; index++)
            {
                result[index] = upper
                    ? char.ToUpperInvariant(value[index])
                    : char.ToLowerInvariant(value[index]);
            }
            return Create(result);
        }

        private static int GetOrdinalIgnoreCaseHashCode(string value)
        {
            var hash = 2_166_136_261;
            for (var index = 0; index < value.Length; index++)
            {
                hash = (hash ^ FoldOrdinalIgnoreCase(value[index])) * 16_777_619;
            }
            hash ^= hash >> 16;
            hash *= 2_246_822_519;
            hash ^= hash >> 13;
            hash *= 3_266_489_917;
            hash ^= hash >> 16;
            return (int)hash;
        }

        private static string[]? ToStringSeparators(char[]? separators)
        {
            if (separators is null || separators.Length == 0)
            {
                return null;
            }
            var result = new string[separators.Length];
            for (var index = 0; index < separators.Length; index++)
            {
                result[index] = separators[index].ToString();
            }
            return result;
        }

        private string[] SplitCore(string[]? separators, int count, StringSplitOptions options)
        {
            if (count < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if ((options & ~(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) != 0)
            {
                throw new ArgumentException(nameof(options));
            }
            if (count == 0)
            {
                return Array.Empty<string>();
            }
            if (count == 1)
            {
                var only = (options & StringSplitOptions.TrimEntries) != 0 ? Trim() : this;
                return (options & StringSplitOptions.RemoveEmptyEntries) != 0 && only.Length == 0
                    ? Array.Empty<string>()
                    : new[] { only };
            }
            separators ??= new[] { " ", "\t", "\r", "\n", "\f", "\v" };
            var result = new Collections.Generic.List<string>();
            var start = 0;
            var index = 0;
            while (index <= Length)
            {
                var matchedLength = 0;
                if (index < Length)
                {
                    for (var separatorIndex = 0; separatorIndex < separators.Length; separatorIndex++)
                    {
                        var separator = separators[separatorIndex];
                        if (separator is not null && separator.Length != 0 && index + separator.Length <= Length &&
                            MatchesAt(this, separator, index, StringComparison.Ordinal))
                        {
                            matchedLength = separator.Length;
                            break;
                        }
                    }
                }
                if (index == Length || matchedLength != 0)
                {
                    var item = Substring(start, index - start);
                    if ((options & StringSplitOptions.TrimEntries) != 0)
                    {
                        item = item.Trim();
                    }
                    if ((options & StringSplitOptions.RemoveEmptyEntries) == 0 || item.Length != 0)
                    {
                        if (count == 1 || (count > 1 && result.Count == count - 1))
                        {
                            var remainder = Substring(start);
                            if ((options & StringSplitOptions.TrimEntries) != 0)
                            {
                                remainder = remainder.Trim();
                            }
                            if ((options & StringSplitOptions.RemoveEmptyEntries) == 0 || remainder.Length != 0)
                            {
                                result.Add(remainder);
                            }
                            return result.ToArray();
                        }
                        result.Add(item);
                    }
                    start = index + matchedLength;
                    index = start;
                    continue;
                }
                index++;
            }
            return result.ToArray();
        }

        private string TrimCore(char[]? trimChars, bool trimStart, bool trimEnd)
        {
            var start = 0;
            var end = Length - 1;
            while (trimStart && start <= end && IsTrimChar(this[start], trimChars))
            {
                start++;
            }
            while (trimEnd && end >= start && IsTrimChar(this[end], trimChars))
            {
                end--;
            }
            return Substring(start, end - start + 1);
        }

        private static bool IsTrimChar(char value, char[]? trimChars)
        {
            if (trimChars is null)
            {
                return IsWhiteSpace(value);
            }
            for (var index = 0; index < trimChars.Length; index++)
            {
                if (value == trimChars[index])
                {
                    return true;
                }
            }
            return false;
        }

        // The static-abstract parse contracts are ordinary identity operations
        // for String, matching the upstream implementation.
        static string IParsable<string>.Parse(string s, IFormatProvider? provider)
        {
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }
            return s;
        }

        static bool IParsable<string>.TryParse(string? s, IFormatProvider? provider, out string result)
        {
            result = s!;
            return s is not null;
        }

        static string ISpanParsable<string>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            Create(s.ToArray());

        static bool ISpanParsable<string>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out string result)
        {
            result = Create(s.ToArray());
            return true;
        }

        IEnumerator<char> IEnumerable<char>.GetEnumerator() => new CharEnumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new CharEnumerator(this);

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this, provider);
        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this, provider);
        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this, provider);
        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this, provider);
        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this, provider);
        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this, provider);
        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this, provider);
        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this, provider);
        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this, provider);
        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this, provider);
        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle(this, provider);
        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble(this, provider);
        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this, provider);
        DateTime IConvertible.ToDateTime(IFormatProvider? provider) => DateTime.Parse(this);
        string IConvertible.ToString(IFormatProvider? provider) => this;
        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => this;

    }


    public enum StringComparison
    {
        Ordinal = 4,
        OrdinalIgnoreCase = 5,
    }
}
