// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;

namespace System.Text
{
    // What string.Format(string, object...) and string.Format(IFormatProvider, string, object...) become where the
    // format is a constant, with up to eight arguments, an array built for params included: the compiler checks that the format is valid and that its holes index only the arguments
    // given, then calls these with the arguments as they were before boxing and a hole type for each (FormattedHole,
    // ItemFormattedHole where a hole names an item format, HexHole where that format is hexadecimal on a primitive
    // integer, ToStringHole). Each hole is formatted in turn as string.Format formats it, a custom formatter first, so
    // the text is the same, without string.Format's parser of item formats and without a boxed argument reaching every
    // type's formatting through ISpanFormattable: a program carries only its argument types' own formatting. The format
    // is read and the text built by CompositeFormatBuilder, which each instance shares.
    internal static class TypedCompositeFormat
    {
        internal static string Format<T0, H0>(string format, T0 arg0)
            where H0 : ICompositeFormatHole<T0>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            while (builder.NextHole() >= 0)
            {
                H0.Append(ref builder, arg0);
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, H0, H1>(string format, T0 arg0, T1 arg1)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                if (index == 0)
                {
                    H0.Append(ref builder, arg0);
                }
                else
                {
                    H1.Append(ref builder, arg1);
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, H0, H1, H2>(string format, T0 arg0, T1 arg1, T2 arg2)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                if (index == 0)
                {
                    H0.Append(ref builder, arg0);
                }
                else if (index == 1)
                {
                    H1.Append(ref builder, arg1);
                }
                else
                {
                    H2.Append(ref builder, arg2);
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, H0>(IFormatProvider? provider, string format, T0 arg0)
            where H0 : ICompositeFormatHole<T0>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            while (builder.NextHole() >= 0)
            {
                H0.Append(ref builder, arg0);
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, H0, H1>(IFormatProvider? provider, string format, T0 arg0, T1 arg1)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                if (index == 0)
                {
                    H0.Append(ref builder, arg0);
                }
                else
                {
                    H1.Append(ref builder, arg1);
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, H0, H1, H2>(IFormatProvider? provider, string format, T0 arg0, T1 arg1, T2 arg2)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                if (index == 0)
                {
                    H0.Append(ref builder, arg0);
                }
                else if (index == 1)
                {
                    H1.Append(ref builder, arg1);
                }
                else
                {
                    H2.Append(ref builder, arg2);
                }
            }
            return builder.ToStringAndClear();
        }

        // Four to eight arguments, as string.Format(string, params object[]) takes them.
        internal static string Format<T0, T1, T2, T3, H0, H1, H2, H3>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    default:
                        H3.Append(ref builder, arg3);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, H0, H1, H2, H3, H4>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    default:
                        H4.Append(ref builder, arg4);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, T5, H0, H1, H2, H3, H4, H5>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
            where H5 : ICompositeFormatHole<T5>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    case 4:
                        H4.Append(ref builder, arg4);
                        break;
                    default:
                        H5.Append(ref builder, arg5);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, T5, T6, H0, H1, H2, H3, H4, H5, H6>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
            where H5 : ICompositeFormatHole<T5>
            where H6 : ICompositeFormatHole<T6>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    case 4:
                        H4.Append(ref builder, arg4);
                        break;
                    case 5:
                        H5.Append(ref builder, arg5);
                        break;
                    default:
                        H6.Append(ref builder, arg6);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, T5, T6, T7, H0, H1, H2, H3, H4, H5, H6, H7>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
            where H5 : ICompositeFormatHole<T5>
            where H6 : ICompositeFormatHole<T6>
            where H7 : ICompositeFormatHole<T7>
        {
            var builder = new CompositeFormatBuilder(format, null, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    case 4:
                        H4.Append(ref builder, arg4);
                        break;
                    case 5:
                        H5.Append(ref builder, arg5);
                        break;
                    case 6:
                        H6.Append(ref builder, arg6);
                        break;
                    default:
                        H7.Append(ref builder, arg7);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, H0, H1, H2, H3>(IFormatProvider? provider, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    default:
                        H3.Append(ref builder, arg3);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, H0, H1, H2, H3, H4>(IFormatProvider? provider, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    default:
                        H4.Append(ref builder, arg4);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, T5, H0, H1, H2, H3, H4, H5>(IFormatProvider? provider, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
            where H5 : ICompositeFormatHole<T5>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    case 4:
                        H4.Append(ref builder, arg4);
                        break;
                    default:
                        H5.Append(ref builder, arg5);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, T5, T6, H0, H1, H2, H3, H4, H5, H6>(IFormatProvider? provider, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
            where H5 : ICompositeFormatHole<T5>
            where H6 : ICompositeFormatHole<T6>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    case 4:
                        H4.Append(ref builder, arg4);
                        break;
                    case 5:
                        H5.Append(ref builder, arg5);
                        break;
                    default:
                        H6.Append(ref builder, arg6);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }

        internal static string Format<T0, T1, T2, T3, T4, T5, T6, T7, H0, H1, H2, H3, H4, H5, H6, H7>(IFormatProvider? provider, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where H0 : ICompositeFormatHole<T0>
            where H1 : ICompositeFormatHole<T1>
            where H2 : ICompositeFormatHole<T2>
            where H3 : ICompositeFormatHole<T3>
            where H4 : ICompositeFormatHole<T4>
            where H5 : ICompositeFormatHole<T5>
            where H6 : ICompositeFormatHole<T6>
            where H7 : ICompositeFormatHole<T7>
        {
            var builder = new CompositeFormatBuilder(format, provider, stackalloc char[CompositeFormatBuilder.StackLength]);
            for (int index; (index = builder.NextHole()) >= 0;)
            {
                switch (index)
                {
                    case 0:
                        H0.Append(ref builder, arg0);
                        break;
                    case 1:
                        H1.Append(ref builder, arg1);
                        break;
                    case 2:
                        H2.Append(ref builder, arg2);
                        break;
                    case 3:
                        H3.Append(ref builder, arg3);
                        break;
                    case 4:
                        H4.Append(ref builder, arg4);
                        break;
                    case 5:
                        H5.Append(ref builder, arg5);
                        break;
                    case 6:
                        H6.Append(ref builder, arg6);
                        break;
                    default:
                        H7.Append(ref builder, arg7);
                        break;
                }
            }
            return builder.ToStringAndClear();
        }
    }

    // The text of a composite format being written: the literal text up to each hole, from the format the compiler
    // encodes from what ValueStringBuilder.AppendFormatHelper reads, then the hole's value as its hole type appends it,
    // padded to the hole's alignment. It writes to a stack buffer and moves to an array of its own when the text outgrows it, so
    // formatting needs no array pool and most texts allocate only the string.
    internal ref struct CompositeFormatBuilder
    {
        internal const int StackLength = 256;

        // The encoding of a hole, as the compiler's CompositeFormatParser writes it: below the surrogates for any of the
        // eight indexes and an alignment from -AlignmentLimit up to AlignmentLimit - 1.
        private const int HoleBase = 0x100;
        private const int AlignmentLimit = 2048;

        private readonly string _format;
        private int _formatPosition;
        private Span<char> _chars;
        private int _length;
        private int _holeStart;

        internal CompositeFormatBuilder(string format, IFormatProvider? provider, Span<char> initial)
        {
            _format = format;
            _chars = initial;
            Provider = provider;
            // Once for the whole format, as string.Format asks for it.
            Formatter = (ICustomFormatter?)provider?.GetFormat(typeof(ICustomFormatter));
        }

        internal readonly IFormatProvider? Provider { get; }

        internal readonly ICustomFormatter? Formatter { get; }

        // The current hole's item format, null for none or an empty one, and its alignment, negative to pad on the
        // right.
        internal string? ItemFormat { get; private set; }

        internal int Alignment { get; private set; }

        // What is left of the buffer after what is written.
        internal readonly Span<char> Free => _chars.Slice(_length);

        internal void Advance(int count) => _length += count;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Grow()
        {
            var larger = new char[_chars.Length * 2];
            _chars.Slice(0, _length).CopyTo(larger);
            _chars = larger;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Append(scoped ReadOnlySpan<char> text)
        {
            while (_chars.Length - _length < text.Length)
            {
                Grow();
            }
            text.CopyTo(_chars.Slice(_length));
            _length += text.Length;
        }

        // Pads what the hole appended since it started to its alignment, as string.Format pads: spaces before it for
        // a positive alignment, after it for a negative one.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Pad()
        {
            var width = Alignment < 0 ? -Alignment : Alignment;
            var padding = width - (_length - _holeStart);
            if (padding <= 0)
            {
                return;
            }
            while (_chars.Length - _length < padding)
            {
                Grow();
            }
            if (Alignment > 0)
            {
                _chars.Slice(_holeStart, _length - _holeStart).CopyTo(_chars.Slice(_holeStart + padding));
                _chars.Slice(_holeStart, padding).Fill(' ');
            }
            else
            {
                _chars.Slice(_length, padding).Fill(' ');
            }
            _length += padding;
        }

        internal readonly string ToStringAndClear() => new string(_chars.Slice(0, _length));

        // Appends the text up to the next hole and reads the hole's index, alignment and item format; -1 at the end of
        // the format. The compiler passes the format encoded: its text as written, with doubled braces, and each hole
        // as '{', one character of HoleBase + index + 8 * (alignment + AlignmentLimit), the item format and '}'. That
        // character is past every brace, so a brace after a brace is an escaped one and anything else opens a hole.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal int NextHole()
        {
            var format = _format;
            var position = _formatPosition;
            while (true)
            {
                if ((uint)position >= (uint)format.Length)
                {
                    _formatPosition = position;
                    return -1;
                }
                var remainder = format.AsSpan(position);
                var brace = remainder.IndexOfAny('{', '}');
                if (brace < 0)
                {
                    Append(remainder);
                    _formatPosition = format.Length;
                    return -1;
                }
                int next = remainder[brace + 1];
                if (next == remainder[brace])
                {
                    Append(remainder.Slice(0, brace + 1));
                    position += brace + 2;
                    continue;
                }
                Append(remainder.Slice(0, brace));
                var start = position + brace + 2;
                var end = start;
                while (format[end] != '}')
                {
                    end++;
                }
                ItemFormat = end > start ? format.Substring(start, end - start) : null;
                next -= HoleBase;
                Alignment = (next >> 3) - AlignmentLimit;
                _holeStart = _length;
                _formatPosition = end + 1;
                return next & 7;
            }
        }

        // What a custom formatter wrote for the hole, padded, as string.Format asks it first; false when it wrote
        // nothing and the hole formats its value itself.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal bool TryAppendCustom(object? value)
        {
            if (Formatter?.Format(ItemFormat, value, Provider) is not { } text)
            {
                return false;
            }
            Append(text);
            Pad();
            return true;
        }
    }

    // How a hole of type T appends its value to the builder, at the builder's current hole.
    internal interface ICompositeFormatHole<T>
    {
        static abstract void Append(ref CompositeFormatBuilder builder, T value);
    }

    // A value formatted as string.Format formats it without an item format, none of the argument's holes naming one:
    // a number by its own formatting without a format string, an ISpanFormattable by its TryFormat, an IFormattable by
    // its ToString, anything else by ToString, null as nothing.
    internal readonly struct FormattedHole<T> : ICompositeFormatHole<T>
    {
        public static void Append(ref CompositeFormatBuilder builder, T value)
        {
            if (builder.Formatter is not null && builder.TryAppendCustom(value))
            {
                return;
            }
            if (value is not null)
            {
                int written;
                if (Number.FormatsWithoutFormat<T>())
                {
                    while (!Number.TryFormatWithoutFormat(value, builder.Provider, builder.Free, out written))
                    {
                        builder.Grow();
                    }
                    builder.Advance(written);
                }
                else if (value is ISpanFormattable)
                {
                    while (!((ISpanFormattable)value).TryFormat(builder.Free, out written, default, builder.Provider))
                    {
                        builder.Grow();
                    }
                    builder.Advance(written);
                }
                else
                {
                    builder.Append(value is IFormattable ? ((IFormattable)value).ToString(null, builder.Provider) : value.ToString());
                }
            }
            builder.Pad();
        }
    }

    // A value of an argument a hole names an item format for, formatted as string.Format formats it with that format;
    // its type's formatting then reads the format string.
    internal readonly struct ItemFormattedHole<T> : ICompositeFormatHole<T>
    {
        public static void Append(ref CompositeFormatBuilder builder, T value)
        {
            if (builder.ItemFormat is null)
            {
                FormattedHole<T>.Append(ref builder, value);
                return;
            }
            if (builder.Formatter is not null && builder.TryAppendCustom(value))
            {
                return;
            }
            if (value is not null)
            {
                if (value is ISpanFormattable)
                {
                    int written;
                    while (!((ISpanFormattable)value).TryFormat(builder.Free, out written, builder.ItemFormat, builder.Provider))
                    {
                        builder.Grow();
                    }
                    builder.Advance(written);
                }
                else
                {
                    builder.Append(value is IFormattable
                        ? ((IFormattable)value).ToString(builder.ItemFormat, builder.Provider)
                        : value.ToString());
                }
            }
            builder.Pad();
        }
    }

    // A reference no IFormattable can be, as the compiler finds for its static type, formatted by its ToString as
    // string.Format formats it whatever its item format, null as nothing.
    internal readonly struct ToStringHole<T> : ICompositeFormatHole<T>
    {
        public static void Append(ref CompositeFormatBuilder builder, T value)
        {
            if (builder.Formatter is not null && builder.TryAppendCustom(value))
            {
                return;
            }
            builder.Append(value?.ToString());
            builder.Pad();
        }
    }

    // A primitive integer whose holes name a hexadecimal item format (X or x, with a precision of two digits at most,
    // as the compiler checked) or none: the hexadecimal digits of its bits at its own width, two's complement for a
    // negative value, upper or lower case as the format's letter, at least as many as the precision and at least one,
    // as the integer's ToString(format) writes them, without reading the format through Number's parser.
    internal readonly struct HexHole<T> : ICompositeFormatHole<T>
    {
        public static void Append(ref CompositeFormatBuilder builder, T value)
        {
            var format = builder.ItemFormat;
            if (format is null)
            {
                FormattedHole<T>.Append(ref builder, value);
                return;
            }
            if (builder.Formatter is not null && builder.TryAppendCustom(value))
            {
                return;
            }
            var bits = HexadecimalDigits.Bits(value);
            Span<char> digits = stackalloc char[HexadecimalDigits.Count(bits, format)];
            HexadecimalDigits.Write(bits, format, digits);
            builder.Append(digits);
            builder.Pad();
        }
    }
}
