// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Buffers.Text
{
    internal static partial class FormattingHelpers
    {
        public static bool TryFormat<T>(T value, Span<byte> utf8Destination, out int bytesWritten, StandardFormat format) where T : struct
        {
            string? formatText = format.IsDefault ? null : format.ToString();
            string text;
            if (typeof(T) == typeof(decimal))
            {
                text = ((decimal)(object)value).ToString();
            }
            else if (typeof(T) == typeof(float))
            {
                text = Number.FormatSingle((float)(object)value, formatText);
            }
            else
            {
                text = Number.FormatDouble((double)(object)value, formatText);
            }

            if (utf8Destination.Length < text.Length)
            {
                bytesWritten = 0;
                return false;
            }

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character > 0x7F)
                {
                    bytesWritten = 0;
                    return false;
                }
                utf8Destination[index] = (byte)character;
            }

            bytesWritten = text.Length;
            return true;
        }

        /// <summary>
        /// Returns the symbol contained within the standard format. If the standard format
        /// has not been initialized, returns the provided fallback symbol.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char GetSymbolOrDefault(in StandardFormat format, char defaultSymbol)
        {
            // This is equivalent to the line below, but it is written in such a way
            // that the JIT is able to perform more optimizations.
            //
            // return (format.IsDefault) ? defaultSymbol : format.Symbol;

            char symbol = format.Symbol;
            if (symbol == default && format.Precision == default)
            {
                symbol = defaultSymbol;
            }
            return symbol;
        }
    }
}
