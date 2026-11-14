// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptations: NetWasm runs upstream's invariant globalization mode only, so a culture's
// values are the invariant field defaults below and the CultureData constructor only initializes
// the sign flags. CurrentInfo reads the current culture's NumberFormatInfo, which CultureInfo
// mirrors here whenever the current culture changes, so that formatting with a null provider does
// not reach CultureInfo; it is InvariantInfo until a culture is assigned. InvariantInfo is the
// read-only instance that CultureInfo.InvariantCulture.NumberFormat also returns. The parse-style
// validators stay in NetWasm's Number.DecimalIeee754.Support.cs. _nativeDigits stays null until
// NativeDigits is set, and the getter builds the ASCII digits, instead of upstream's static
// s_asciiDigits array, so constructing a NumberFormatInfo does not initialize ten digit strings.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Globalization
{
    /// <remarks>
    /// Property             Default Description
    /// PositiveSign           '+'   Character used to indicate positive values.
    /// NegativeSign           '-'   Character used to indicate negative values.
    /// NumberDecimalSeparator '.'   The character used as the decimal separator.
    /// NumberGroupSeparator   ','   The character used to separate groups of
    ///                              digits to the left of the decimal point.
    /// NumberDecimalDigits    2     The default number of decimal places.
    /// NumberGroupSizes       3     The number of digits in each group to the
    ///                              left of the decimal point.
    /// NaNSymbol             "NaN"  The string used to represent NaN values.
    /// PositiveInfinitySymbol"Infinity" The string used to represent positive
    ///                              infinities.
    /// NegativeInfinitySymbol"-Infinity" The string used to represent negative
    ///                              infinities.
    ///
    /// Property                  Default  Description
    /// CurrencyDecimalSeparator  '.'      The character used as the decimal
    ///                                    separator.
    /// CurrencyGroupSeparator    ','      The character used to separate groups
    ///                                    of digits to the left of the decimal
    ///                                    point.
    /// CurrencyDecimalDigits     2        The default number of decimal places.
    /// CurrencyGroupSizes        3        The number of digits in each group to
    ///                                    the left of the decimal point.
    /// CurrencyPositivePattern   0        The format of positive values.
    /// CurrencyNegativePattern   0        The format of negative values.
    /// CurrencySymbol            "$"      String used as local monetary symbol.
    /// </remarks>
    public sealed partial class NumberFormatInfo : IFormatProvider, ICloneable
    {
        internal static readonly int[] s_intArrayWithElement3 = [3];

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

        internal string[]? _nativeDigits; // NetWasm: null means the ASCII digits; only copies are handed out

        internal int _numberDecimalDigits = 2;
        internal int _currencyDecimalDigits = 2;
        internal int _currencyPositivePattern;
        internal int _currencyNegativePattern;
        internal int _numberNegativePattern = 1;
        internal int _percentPositivePattern;
        internal int _percentNegativePattern;
        internal int _percentDecimalDigits = 2;

        internal int _digitSubstitution = (int)DigitShapes.None;

        internal bool _isReadOnly;

        private bool _hasInvariantNumberSigns = true;

        // When _allowHyphenDuringParsing is set to true, we'll allow the number parser to accept the hyphen `-` U+002D as a negative sign even if the culture
        // negative sign is not the hyphen. For example, the Swedish culture (e.g. "sv-SE") has U+2212 as the negative sign.
        private bool _allowHyphenDuringParsing;

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

        public NumberFormatInfo()
        {
        }

        private static void VerifyNativeDigits(string[] nativeDig, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(nativeDig);

            if (nativeDig.Length != 10)
            {
                throw new ArgumentException(SR.Argument_InvalidNativeDigitCount, propertyName);
            }

            for (int i = 0; i < nativeDig.Length; i++)
            {
                if (nativeDig[i] == null)
                {
                    throw new ArgumentNullException(propertyName, SR.ArgumentNull_ArrayValue);
                }

                if (nativeDig[i].Length != 1)
                {
                    if (nativeDig[i].Length != 2)
                    {
                        // Not 1 or 2 UTF-16 code points
                        throw new ArgumentException(SR.Argument_InvalidNativeDigitValue, propertyName);
                    }
                    else if (!char.IsSurrogatePair(nativeDig[i][0], nativeDig[i][1]))
                    {
                        // 2 UTF-6 code points, but not a surrogate pair
                        throw new ArgumentException(SR.Argument_InvalidNativeDigitValue, propertyName);
                    }
                }

                if (CharUnicodeInfo.GetDecimalDigitValue(nativeDig[i], 0) != i &&
                    CharUnicodeInfo.GetUnicodeCategory(nativeDig[i], 0) != UnicodeCategory.PrivateUse)
                {
                    // Not the appropriate digit according to the Unicode data properties
                    // (Digit 0 must be a 0, etc.).
                    throw new ArgumentException(SR.Argument_InvalidNativeDigitValue, propertyName);
                }
            }
        }

        private static void VerifyDigitSubstitution(DigitShapes digitSub, string propertyName)
        {
            switch (digitSub)
            {
                case DigitShapes.Context:
                case DigitShapes.None:
                case DigitShapes.NativeNational:
                    // Success.
                    break;

                default:
                    throw new ArgumentException(SR.Argument_InvalidDigitSubstitution, propertyName);
            }
        }

        internal bool HasInvariantNumberSigns => _hasInvariantNumberSigns;
        internal bool AllowHyphenDuringParsing() => _allowHyphenDuringParsing;

        private void InitializeInvariantAndNegativeSignFlags()
        {
            _hasInvariantNumberSigns = _positiveSign == "+" && _negativeSign == "-";

            // The list of the Minus characters are picked up from the CLDR parse lenient data.
            // e.g. https://github.com/unicode-org/cldr/blob/feb602b06bd18ba7333464bd648b68292e8aa54d/common/main/sw.xml#L1001

            _allowHyphenDuringParsing = _negativeSign.Length == 1 &&
                                        _negativeSign[0] switch
                                        {
                                            '\u2012' or         // Figure Dash
                                            '\u207B' or         // Superscript Minus
                                            '\u208B' or         // Subscript Minus
                                            '\u2212' or         // Minus Sign
                                            '\u2796' or         // Heavy Minus Sign
                                            '\uFE63' or         // Small Hyphen-Minus
                                            '\uFF0D' => true,   // Fullwidth Hyphen-Minus
                                            _ => false
                                        };
        }

        internal NumberFormatInfo(CultureData? cultureData)
        {
            if (cultureData != null)
            {
                // The invariant culture's values are this type's field defaults.

                InitializeInvariantAndNegativeSignFlags();
            }
        }

        private void VerifyWritable()
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException(SR.InvalidOperation_ReadOnly);
            }
        }

        /// <summary>
        /// Returns a default NumberFormatInfo that will be universally
        /// supported and constant irrespective of the current culture.
        /// Used by FromString methods.
        /// </summary>
        private static NumberFormatInfo? s_invariantInfo;

        // NetWasm: the symbols formatting with a null provider needs, read without constructing a NumberFormatInfo
        // (owner decision 2026-10-25): the assigned culture's when one is current, else the invariant constants.
        // They are what CurrentInfo would report, since InvariantInfo is read-only.
        internal static string CurrentNegativeSign => CurrentNumberFormatInfo.Value?.NegativeSign ?? InvariantNegativeSign;
        internal static string CurrentNaNSymbol => CurrentNumberFormatInfo.Value?.NaNSymbol ?? InvariantNaNSymbol;
        internal static string CurrentPositiveInfinitySymbol => CurrentNumberFormatInfo.Value?.PositiveInfinitySymbol ?? InvariantPositiveInfinitySymbol;
        internal static string CurrentNegativeInfinitySymbol => CurrentNumberFormatInfo.Value?.NegativeInfinitySymbol ?? InvariantNegativeInfinitySymbol;

        internal static ReadOnlySpan<TChar> CurrentNegativeSignTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar> =>
            CurrentNumberFormatInfo.Value is { } info ? info.NegativeSignTChar<TChar>() : InvariantTChar<TChar>(InvariantNegativeSign, "-"u8);

        internal static ReadOnlySpan<TChar> CurrentPositiveSignTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar> =>
            CurrentNumberFormatInfo.Value is { } info ? info.PositiveSignTChar<TChar>() : InvariantTChar<TChar>("+", "+"u8);

        internal static ReadOnlySpan<TChar> CurrentNumberDecimalSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar> =>
            CurrentNumberFormatInfo.Value is { } info ? info.NumberDecimalSeparatorTChar<TChar>() : InvariantTChar<TChar>(".", "."u8);

        private static ReadOnlySpan<TChar> InvariantTChar<TChar>(string text, ReadOnlySpan<byte> utf8) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(text) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(utf8);
        }

        public static NumberFormatInfo InvariantInfo => s_invariantInfo ??= new NumberFormatInfo { _isReadOnly = true };

        public static NumberFormatInfo GetInstance(IFormatProvider? formatProvider)
        {
            return formatProvider == null ?
                CurrentInfo : // Fast path for a null provider
                GetProviderNonNull(formatProvider);

            static NumberFormatInfo GetProviderNonNull(IFormatProvider provider)
            {
                // Fast path for a regular CultureInfo
                if (provider.GetType() == typeof(CultureInfo) && ((CultureInfo)provider)._numInfo is { } info)
                {
                    return info;
                }

                return
                    provider as NumberFormatInfo ?? // Fast path for an NFI
                    provider.GetFormat(typeof(NumberFormatInfo)) as NumberFormatInfo ??
                    CurrentInfo;
            }
        }

        public object Clone()
        {
            NumberFormatInfo n = (NumberFormatInfo)MemberwiseClone();
            n._isReadOnly = false;
            return n;
        }

        public int CurrencyDecimalDigits
        {
            get => _currencyDecimalDigits;
            set
            {
                if (value < 0 || value > 99)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 99);
                }

                VerifyWritable();
                _currencyDecimalDigits = value;
            }
        }

        public string CurrencyDecimalSeparator
        {
            get => _currencyDecimalSeparator;
            set
            {
                VerifyWritable();
                ArgumentException.ThrowIfNullOrEmpty(value);
                _currencyDecimalSeparator = value;
                _currencyDecimalSeparatorUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> CurrencyDecimalSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_currencyDecimalSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _currencyDecimalSeparatorUtf8, _currencyDecimalSeparator));
        }

        public bool IsReadOnly => _isReadOnly;

        /// <summary>
        /// Check the values of the groupSize array.
        /// Every element in the groupSize array should be between 1 and 9
        /// except the last element could be zero.
        /// </summary>
        internal static void CheckGroupSize(string propName, int[] groupSize)
        {
            for (int i = 0; i < groupSize.Length; i++)
            {
                if (groupSize[i] < 1)
                {
                    if (i == groupSize.Length - 1 && groupSize[i] == 0)
                    {
                        return;
                    }

                    throw new ArgumentException(SR.Argument_InvalidGroupSize, propName);
                }
                else if (groupSize[i] > 9)
                {
                    throw new ArgumentException(SR.Argument_InvalidGroupSize, propName);
                }
            }
        }

        public int[] CurrencyGroupSizes
        {
            get => (int[])_currencyGroupSizes.Clone();
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();

                int[] inputSizes = (int[])value.Clone();
                CheckGroupSize(nameof(value), inputSizes);
                _currencyGroupSizes = inputSizes;
            }
        }

        public int[] NumberGroupSizes
        {
            get => (int[])_numberGroupSizes.Clone();
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();

                int[] inputSizes = (int[])value.Clone();
                CheckGroupSize(nameof(value), inputSizes);
                _numberGroupSizes = inputSizes;
            }
        }

        public int[] PercentGroupSizes
        {
            get => (int[])_percentGroupSizes.Clone();
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                int[] inputSizes = (int[])value.Clone();
                CheckGroupSize(nameof(value), inputSizes);
                _percentGroupSizes = inputSizes;
            }
        }

        public string CurrencyGroupSeparator
        {
            get => _currencyGroupSeparator;
            set
            {
                VerifyWritable();
                ArgumentNullException.ThrowIfNull(value);
                _currencyGroupSeparator = value;
                _currencyGroupSeparatorUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> CurrencyGroupSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_currencyGroupSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _currencyGroupSeparatorUtf8, _currencyGroupSeparator));
        }

        public string CurrencySymbol
        {
            get => _currencySymbol;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _currencySymbol = value;
                _currencySymbolUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> CurrencySymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_currencySymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _currencySymbolUtf8, _currencySymbol));
        }

        /// <summary>
        /// Returns the current culture's NumberFormatInfo. Used by Parse methods.
        /// </summary>

        public static NumberFormatInfo CurrentInfo => CurrentNumberFormatInfo.Value ?? InvariantInfo;

        public string NaNSymbol
        {
            get => _nanSymbol;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _nanSymbol = value;
                _nanSymbolUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NaNSymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_nanSymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _nanSymbolUtf8, _nanSymbol));
        }

        public int CurrencyNegativePattern
        {
            get => _currencyNegativePattern;
            set
            {
                if (value < 0 || value > 16)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 16);
                }

                VerifyWritable();
                _currencyNegativePattern = value;
            }
        }

        public int NumberNegativePattern
        {
            get => _numberNegativePattern;
            set
            {
                // NOTENOTE: the range of value should correspond to negNumberFormats[] in vm\COMNumber.cpp.
                if (value < 0 || value > 4)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 4);
                }

                VerifyWritable();
                _numberNegativePattern = value;
            }
        }

        public int PercentPositivePattern
        {
            get => _percentPositivePattern;
            set
            {
                // NOTENOTE: the range of value should correspond to posPercentFormats[] in vm\COMNumber.cpp.
                if (value < 0 || value > 3)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 3);
                }

                VerifyWritable();
                _percentPositivePattern = value;
            }
        }

        public int PercentNegativePattern
        {
            get => _percentNegativePattern;
            set
            {
                // NOTENOTE: the range of value should correspond to posPercentFormats[] in vm\COMNumber.cpp.
                if (value < 0 || value > 11)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 11);
                }

                VerifyWritable();
                _percentNegativePattern = value;
            }
        }

        public string NegativeInfinitySymbol
        {
            get => _negativeInfinitySymbol;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _negativeInfinitySymbol = value;
                _negativeInfinitySymbolUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NegativeInfinitySymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_negativeInfinitySymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _negativeInfinitySymbolUtf8, _negativeInfinitySymbol));
        }

        public string NegativeSign
        {
            get => _negativeSign;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _negativeSign = value;
                _negativeSignUtf8 = null;
                InitializeInvariantAndNegativeSignFlags();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NegativeSignTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_negativeSign) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _negativeSignUtf8, _negativeSign));
        }

        public int NumberDecimalDigits
        {
            get => _numberDecimalDigits;
            set
            {
                if (value < 0 || value > 99)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 99);
                }

                VerifyWritable();
                _numberDecimalDigits = value;
            }
        }

        public string NumberDecimalSeparator
        {
            get => _numberDecimalSeparator;
            set
            {
                VerifyWritable();
                ArgumentException.ThrowIfNullOrEmpty(value);
                _numberDecimalSeparator = value;
                _numberDecimalSeparatorUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NumberDecimalSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_numberDecimalSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _numberDecimalSeparatorUtf8, _numberDecimalSeparator));
        }

        public string NumberGroupSeparator
        {
            get => _numberGroupSeparator;
            set
            {
                VerifyWritable();
                ArgumentNullException.ThrowIfNull(value);
                _numberGroupSeparator = value;
                _numberGroupSeparatorUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> NumberGroupSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_numberGroupSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _numberGroupSeparatorUtf8, _numberGroupSeparator));
        }

        public int CurrencyPositivePattern
        {
            get => _currencyPositivePattern;
            set
            {
                if (value < 0 || value > 3)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 3);
                }

                VerifyWritable();
                _currencyPositivePattern = value;
            }
        }

        public string PositiveInfinitySymbol
        {
            get => _positiveInfinitySymbol;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _positiveInfinitySymbol = value;
                _positiveInfinitySymbolUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PositiveInfinitySymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_positiveInfinitySymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _positiveInfinitySymbolUtf8, _positiveInfinitySymbol));
        }

        public string PositiveSign
        {
            get => _positiveSign;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _positiveSign = value;
                _positiveSignUtf8 = null;
                InitializeInvariantAndNegativeSignFlags();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PositiveSignTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_positiveSign) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _positiveSignUtf8, _positiveSign));
        }

        public int PercentDecimalDigits
        {
            get => _percentDecimalDigits;
            set
            {
                if (value < 0 || value > 99)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_Range(nameof(value), value, 0, 99);
                }

                VerifyWritable();
                _percentDecimalDigits = value;
            }
        }

        public string PercentDecimalSeparator
        {
            get => _percentDecimalSeparator;
            set
            {
                VerifyWritable();
                ArgumentException.ThrowIfNullOrEmpty(value);
                _percentDecimalSeparator = value;
                _percentDecimalSeparatorUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PercentDecimalSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_percentDecimalSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _percentDecimalSeparatorUtf8, _percentDecimalSeparator));
        }

        public string PercentGroupSeparator
        {
            get => _percentGroupSeparator;
            set
            {
                VerifyWritable();
                ArgumentNullException.ThrowIfNull(value);
                _percentGroupSeparator = value;
                _percentGroupSeparatorUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PercentGroupSeparatorTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_percentGroupSeparator) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _percentGroupSeparatorUtf8, _percentGroupSeparator));
        }

        public string PercentSymbol
        {
            get => _percentSymbol;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                VerifyWritable();
                _percentSymbol = value;
                _percentSymbolUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PercentSymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_percentSymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _percentSymbolUtf8, _percentSymbol));
        }

        public string PerMilleSymbol
        {
            get => _perMilleSymbol;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _perMilleSymbol = value;
                _perMilleSymbolUtf8 = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<TChar> PerMilleSymbolTChar<TChar>() where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            return typeof(TChar) == typeof(char) ?
                Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(_perMilleSymbol) :
                Unsafe.BitCast<ReadOnlySpan<byte>, ReadOnlySpan<TChar>>(GetUtf8Span(ref _perMilleSymbolUtf8, _perMilleSymbol));
        }

        public string[] NativeDigits
        {
            get => _nativeDigits is { } digits ? (string[])digits.Clone() : ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];
            set
            {
                VerifyWritable();
                VerifyNativeDigits(value, nameof(value));
                _nativeDigits = value;
            }
        }

        public DigitShapes DigitSubstitution
        {
            get => (DigitShapes)_digitSubstitution;
            set
            {
                VerifyWritable();
                VerifyDigitSubstitution(value, nameof(value));
                _digitSubstitution = (int)value;
            }
        }

        public object? GetFormat(Type? formatType)
        {
            return formatType == typeof(NumberFormatInfo) ? this : null;
        }

        public static NumberFormatInfo ReadOnly(NumberFormatInfo nfi)
        {
            ArgumentNullException.ThrowIfNull(nfi);

            if (nfi.IsReadOnly)
            {
                return nfi;
            }

            NumberFormatInfo info = (NumberFormatInfo)(nfi.MemberwiseClone());
            info._isReadOnly = true;
            return info;
        }
    }

    // NetWasm: the current culture's NumberFormatInfo; null while the invariant culture is current. It lives in a class
    // without static initializers, so formatting with a null provider reads it without running NumberFormatInfo's (and a
    // program that never assigns a culture reads it as the constant null).
    internal static class CurrentNumberFormatInfo
    {
        internal static NumberFormatInfo? Value;
    }
}
