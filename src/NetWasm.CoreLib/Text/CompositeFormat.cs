// Portions derived from dotnet/runtime System.Private.CoreLib CompositeFormat.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace System.Text
{
    /// <summary>Represents a parsed composite format string.</summary>
    public sealed class CompositeFormat
    {
        private readonly (string? Literal, int ArgIndex, int Alignment, string? Format)[] _segments;
        private readonly int _minimumArgumentCount;

        private CompositeFormat(
            string format,
            (string? Literal, int ArgIndex, int Alignment, string? Format)[] segments)
        {
            Format = format;
            _segments = segments;
            var minimumArgumentCount = 0;
            foreach (var segment in segments)
            {
                if (segment.ArgIndex >= 0)
                {
                    minimumArgumentCount = Math.Max(minimumArgumentCount, segment.ArgIndex + 1);
                }
            }
            _minimumArgumentCount = minimumArgumentCount;
        }

        public string Format { get; }
        public int MinimumArgumentCount
        {
            get { return _minimumArgumentCount; }
        }

        internal (string? Literal, int ArgIndex, int Alignment, string? Format)[] Segments => _segments;

        public static CompositeFormat Parse(
            [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format)
        {
            if (format is null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            var segments = new List<(string? Literal, int ArgIndex, int Alignment, string? Format)>();
            var literal = new StringBuilder();
            var position = 0;
            while (position < format.Length)
            {
                var current = format[position];
                if (current == '{')
                {
                    if (position + 1 < format.Length && format[position + 1] == '{')
                    {
                        literal.Append('{');
                        position += 2;
                        continue;
                    }

                    segments.Add((literal.ToString(), -1, 0, null));
                    literal.Clear();
                    position++;
                    var argumentIndex = ParseIndex(format, ref position);
                    SkipSpaces(format, ref position);

                    var alignment = 0;
                    if (position < format.Length && format[position] == ',')
                    {
                        position++;
                        SkipSpaces(format, ref position);
                        var negative = false;
                        if (position < format.Length && format[position] == '-')
                        {
                            negative = true;
                            position++;
                        }
                        alignment = ParseDigits(format, ref position);
                        alignment = negative ? -alignment : alignment;
                        SkipSpaces(format, ref position);
                    }

                    string? itemFormat = null;
                    if (position < format.Length && format[position] == ':')
                    {
                        position++;
                        var start = position;
                        while (position < format.Length && format[position] != '}')
                        {
                            if (format[position] == '{')
                            {
                                throw new FormatException();
                            }
                            position++;
                        }
                        if (position >= format.Length)
                        {
                            throw new FormatException();
                        }
                        itemFormat = format.Substring(start, position - start);
                    }

                    if (position >= format.Length || format[position] != '}')
                    {
                        throw new FormatException();
                    }
                    position++;
                    segments.Add((null, argumentIndex, alignment, itemFormat));
                    continue;
                }

                if (current == '}')
                {
                    if (position + 1 < format.Length && format[position + 1] == '}')
                    {
                        literal.Append('}');
                        position += 2;
                        continue;
                    }
                    throw new FormatException();
                }

                literal.Append(current);
                position++;
            }

            segments.Add((literal.ToString(), -1, 0, null));
            return new CompositeFormat(format, segments.ToArray());
        }

        internal void ValidateNumberOfArgs(int argumentCount)
        {
            if (argumentCount < _minimumArgumentCount)
            {
                throw new FormatException();
            }
        }

        private static int ParseIndex(string format, ref int position)
        {
            SkipSpaces(format, ref position);
            var value = ParseDigits(format, ref position);
            SkipSpaces(format, ref position);
            return value;
        }

        private static int ParseDigits(string format, ref int position)
        {
            if (position >= format.Length || !IsAsciiDigit(format[position]))
            {
                throw new FormatException();
            }
            var value = 0;
            while (position < format.Length && IsAsciiDigit(format[position]))
            {
                var digit = format[position++] - '0';
                if (value > (int.MaxValue - digit) / 10)
                {
                    throw new FormatException();
                }
                value = value * 10 + digit;
            }
            return value;
        }

        private static void SkipSpaces(string format, ref int position)
        {
            while (position < format.Length && format[position] == ' ')
            {
                position++;
            }
        }

        private static bool IsAsciiDigit(char value) => value >= '0' && value <= '9';
    }
}
