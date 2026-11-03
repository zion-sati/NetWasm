// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Invariant formatting data and UTF accessors retained from upstream NumberFormatInfo.
// Culture data loading and mutable culture configuration remain outside this profile.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Globalization
{
    public sealed partial class NumberFormatInfo
    {
        internal static readonly int[] s_intArrayWithElement3 = [3];

        // Upstream allows the hyphen as a negative sign where the culture's sign
        // is one of the minus characters CLDR's lenient parsing lists; the
        // invariant sign is "-" itself.
        internal bool AllowHyphenDuringParsing() =>
            _negativeSign.Length == 1 &&
            _negativeSign[0] is '\u2012' or '\u207B' or '\u208B' or '\u2212' or '\u2796' or '\uFE63' or '\uFF0D';

        internal int[] _numberGroupSizes = s_intArrayWithElement3; // default to [3]; only clones of this array are handed out
        internal int[] _currencyGroupSizes = s_intArrayWithElement3; // default to [3]; only clones of this array are handed out
        internal int[] _percentGroupSizes = s_intArrayWithElement3; // default to [3]; only clones of this array are handed out
        internal string _positiveSign = "+";
        internal string _negativeSign = "-";
        internal string _numberDecimalSeparator = ".";
        internal string _numberGroupSeparator = ",";
        internal string _currencyGroupSeparator = ",";
        internal string _currencyDecimalSeparator = ".";
        internal string _currencySymbol = "\x00a4";  // U+00a4 is the symbol for International Monetary Fund.
        internal string _nanSymbol = "NaN";
        internal string _positiveInfinitySymbol = "Infinity";
        internal string _negativeInfinitySymbol = "-Infinity";
        internal string _percentDecimalSeparator = ".";
        internal string _percentGroupSeparator = ",";
        internal string _percentSymbol = "%";
        internal string _perMilleSymbol = "\u2030";

        internal byte[]? _positiveSignUtf8;
        internal byte[]? _negativeSignUtf8;
        internal byte[]? _currencySymbolUtf8;
        internal byte[]? _numberDecimalSeparatorUtf8;
        internal byte[]? _currencyDecimalSeparatorUtf8;
        internal byte[]? _currencyGroupSeparatorUtf8;
        internal byte[]? _numberGroupSeparatorUtf8;
        internal byte[]? _percentSymbolUtf8;
        internal byte[]? _percentDecimalSeparatorUtf8;
        internal byte[]? _percentGroupSeparatorUtf8;
        internal byte[]? _perMilleSymbolUtf8;
        internal byte[]? _nanSymbolUtf8;
        internal byte[]? _positiveInfinitySymbolUtf8;
        internal byte[]? _negativeInfinitySymbolUtf8;


        // Each UTF-8 cache is allocated one byte longer than the text it holds and the returned span
        // excludes that trailing NUL. Consumers should still bound themselves by the span length; the
        // terminator only exists so that a helper which scans for one cannot run off the end.
        private static ReadOnlySpan<byte> GetUtf8Span(ref byte[]? utf8Cache, string value)
        {
            byte[] utf8 = utf8Cache ?? CreateUtf8Cache(ref utf8Cache, value);
            Debug.Assert(utf8.Length > 0);
            return MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(utf8), utf8.Length - 1);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte[] CreateUtf8Cache(ref byte[]? utf8Cache, string value)
        {
            byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(value) + 1];
            Encoding.UTF8.GetBytes(value, utf8);
            return utf8Cache = utf8;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> CurrencyDecimalSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_currencyDecimalSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _currencyDecimalSeparatorUtf8, _currencyDecimalSeparator));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> CurrencyGroupSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_currencyGroupSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _currencyGroupSeparatorUtf8, _currencyGroupSeparator));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> CurrencySymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_currencySymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _currencySymbolUtf8, _currencySymbol));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NaNSymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_nanSymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _nanSymbolUtf8, _nanSymbol));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NegativeInfinitySymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_negativeInfinitySymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _negativeInfinitySymbolUtf8, _negativeInfinitySymbol));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NegativeSignTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_negativeSign) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _negativeSignUtf8, _negativeSign));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NumberDecimalSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_numberDecimalSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _numberDecimalSeparatorUtf8, _numberDecimalSeparator));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NumberGroupSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_numberGroupSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _numberGroupSeparatorUtf8, _numberGroupSeparator));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PositiveInfinitySymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_positiveInfinitySymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _positiveInfinitySymbolUtf8, _positiveInfinitySymbol));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PositiveSignTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_positiveSign) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _positiveSignUtf8, _positiveSign));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PercentDecimalSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_percentDecimalSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _percentDecimalSeparatorUtf8, _percentDecimalSeparator));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PercentGroupSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_percentGroupSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _percentGroupSeparatorUtf8, _percentGroupSeparator));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PercentSymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_percentSymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _percentSymbolUtf8, _percentSymbol));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PerMilleSymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_perMilleSymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _perMilleSymbolUtf8, _perMilleSymbol));
        }

        public int CurrencyNegativePattern => 0;
        public int CurrencyPositivePattern => 0;
        public int NumberNegativePattern => 1;
        public int PercentNegativePattern => 0;
        public int PercentPositivePattern => 0;
    }
}
