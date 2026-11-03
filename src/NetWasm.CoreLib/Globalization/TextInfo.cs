// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.
// NetWasm adaptations: NetWasm runs upstream's invariant globalization mode only, so casing is
// invariant-mode casing (NetWasm's invariant char and string casing over the pinned Unicode
// tables) and the culture data is the invariant CultureData stand-in. Upstream's ICU/NLS casing
// paths and span helpers are not ported. IDeserializationCallback throws PNSE, as upstream's does.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text;

namespace System.Globalization
{
    /// <summary>
    /// This Class defines behaviors specific to a writing system.
    /// A writing system is the collection of scripts and orthographic rules
    /// required to represent a language as text.
    /// </summary>
    public sealed partial class TextInfo : ICloneable, IDeserializationCallback
    {
        private bool _isReadOnly;

        private readonly string _cultureName;
        private readonly CultureData _cultureData;

        // // Name of the text info we're using (ie: _cultureData.TextInfoName)
        private readonly string _textInfoName;

        private string? _listSeparator;

        // Invariant text info
        internal static readonly TextInfo Invariant = new TextInfo(CultureData.Invariant, readOnly: true);

        internal TextInfo(CultureData cultureData)
        {
            // This is our primary data source, we don't need most of the rest of this
            _cultureData = cultureData;
            _cultureName = _cultureData.CultureName;
            _textInfoName = _cultureData.TextInfoName;
        }

        private TextInfo(CultureData cultureData, bool readOnly)
            : this(cultureData)
        {
            SetReadOnlyState(readOnly);
        }

        // Upstream's invariant casing: NetWasm runs the invariant mode only.
        internal void ChangeCaseToLower(ReadOnlySpan<char> source, Span<char> destination) =>
            InvariantModeCasing.ToLower(source, destination);

        internal void ChangeCaseToUpper(ReadOnlySpan<char> source, Span<char> destination) =>
            InvariantModeCasing.ToUpper(source, destination);

        internal static string ToLowerInvariant(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            return InvariantModeCasing.ToLower(str);
        }

        internal static string ToUpperInvariant(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            return InvariantModeCasing.ToUpper(str);
        }

        void IDeserializationCallback.OnDeserialization(object? sender)
        {
            throw new PlatformNotSupportedException();
        }

        public int ANSICodePage => _cultureData.ANSICodePage;

        public int OEMCodePage => _cultureData.OEMCodePage;

        public int MacCodePage => _cultureData.MacCodePage;

        public int EBCDICCodePage => _cultureData.EBCDICCodePage;

        // Just use the LCID from our text info name
        public int LCID => CultureInfo.GetCultureInfo(_textInfoName).LCID;

        public string CultureName => _textInfoName;

        public bool IsReadOnly => _isReadOnly;

        public object Clone()
        {
            TextInfo clone = Copy();
            clone.SetReadOnlyState(false);
            return clone;
        }

        // MemberwiseClone, field by field.
        private TextInfo Copy() => new TextInfo(_cultureData) { _isReadOnly = _isReadOnly, _listSeparator = _listSeparator };

        /// <summary>
        /// Create a cloned readonly instance or return the input one if it is
        /// readonly.
        /// </summary>
        public static TextInfo ReadOnly(TextInfo textInfo)
        {
            ArgumentNullException.ThrowIfNull(textInfo);

            if (textInfo.IsReadOnly)
            {
                return textInfo;
            }

            TextInfo clonedTextInfo = textInfo.Copy();
            clonedTextInfo.SetReadOnlyState(true);
            return clonedTextInfo;
        }

        private void VerifyWritable()
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException(SR.InvalidOperation_ReadOnly);
            }
        }

        internal void SetReadOnlyState(bool readOnly)
        {
            _isReadOnly = readOnly;
        }

        /// <summary>
        /// Returns the string used to separate items in a list.
        /// </summary>
        public string ListSeparator
        {
            get => _listSeparator ??= _cultureData.ListSeparator;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _listSeparator = value;
            }
        }

        /// <summary>
        /// Converts the character or string to lower case.  Certain locales
        /// have different casing semantics from the file systems in Win32.
        /// </summary>
        public char ToLower(char c) => char.ToLowerInvariant(c);

        public string ToLower(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            return InvariantModeCasing.ToLower(str);
        }

        internal static string ToLowerAsciiInvariant(string s)
        {
            if (s.Length == 0)
            {
                return string.Empty;
            }

            int i = s.AsSpan().IndexOfAnyInRange('A', 'Z');
            if (i < 0)
            {
                return s;
            }

            char[] result = s.ToCharArray();
            for (; i < result.Length; i++)
            {
                result[i] = ToLowerAsciiInvariant(result[i]);
            }

            return new string(result);
        }

        private static char ToLowerAsciiInvariant(char c)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                // on x86, extending BYTE -> DWORD is more efficient than WORD -> DWORD
                c = (char)(byte)(c | 0x20);
            }
            return c;
        }

        /// <summary>
        /// Converts the character or string to upper case.  Certain locales
        /// have different casing semantics from the file systems in Win32.
        /// </summary>
        public char ToUpper(char c) => char.ToUpperInvariant(c);

        public string ToUpper(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            return InvariantModeCasing.ToUpper(str);
        }

        /// <summary>
        /// Returns true if the dominant direction of text and UI such as the
        /// relative position of buttons and scroll bars
        /// </summary>
        public bool IsRightToLeft => _cultureData.IsRightToLeft;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is TextInfo otherTextInfo
                && CultureName.Equals(otherTextInfo.CultureName);
        }

        public override int GetHashCode() => CultureName.GetHashCode();

        public override string ToString()
        {
            return "TextInfo - " + _cultureData.CultureName;
        }

        /// <summary>
        /// Titlecasing refers to a casing practice wherein the first letter of a word is an uppercase letter
        /// and the rest of the letters are lowercase.  The choice of which words to titlecase in headings
        /// and titles is dependent on language and local conventions.  For example, "The Merry Wives of Windor"
        /// is the appropriate titlecasing of that play's name in English, with the word "of" not titlecased.
        /// In German, however, the title is "Die lustigen Weiber von Windsor," and both "lustigen" and "von"
        /// are not titlecased.  In French even fewer words are titlecased: "Les joyeuses commeres de Windsor."
        ///
        /// Moreover, the determination of what actually constitutes a word is language dependent, and this can
        /// influence which letter or letters of a "word" are uppercased when titlecasing strings.  For example
        /// "l'arbre" is considered two words in French, whereas "can't" is considered one word in English.
        /// </summary>
        public string ToTitleCase(string str)
        {
            ArgumentNullException.ThrowIfNull(str);

            if (str.Length == 0)
            {
                return str;
            }

            StringBuilder result = new StringBuilder();
            string? lowercaseData = null;
            // Store if the current culture is Dutch (special case)
            bool isDutchCulture = CultureName.StartsWith("nl-", StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < str.Length; i++)
            {
                UnicodeCategory charType = GetUnicodeCategoryInternal(str, i, out int charLen);
                if (IsLetterCategory(charType))
                {
                    // Special case to check for Dutch specific titlecasing with "IJ" characters
                    // at the beginning of a word
                    if (isDutchCulture && i < str.Length - 1 && (str[i] == 'i' || str[i] == 'I') && (str[i + 1] == 'j' || str[i + 1] == 'J'))
                    {
                        result.Append("IJ");
                        i += 2;
                    }
                    else
                    {
                        // Do the titlecasing for the first character of the word.
                        i = AddTitlecaseLetter(ref result, ref str, i, charLen) + 1;
                    }

                    // Convert the characters until the end of the this word
                    // to lowercase.
                    int lowercaseStart = i;

                    // Use hasLowerCase flag to prevent from lowercasing acronyms (like "URT", "USA", etc)
                    // This is in line with Word 2000 behavior of titlecasing.
                    bool hasLowerCase = (charType == UnicodeCategory.LowercaseLetter);

                    // Use a loop to find all of the other letters following this letter.
                    while (i < str.Length)
                    {
                        charType = GetUnicodeCategoryInternal(str, i, out charLen);
                        if (IsLetterCategory(charType))
                        {
                            if (charType == UnicodeCategory.LowercaseLetter)
                            {
                                hasLowerCase = true;
                            }
                            i += charLen;
                        }
                        else if (str[i] == '\'')
                        {
                            i++;
                            if (hasLowerCase)
                            {
                                lowercaseData ??= ToLower(str);
                                result.Append(lowercaseData, lowercaseStart, i - lowercaseStart);
                            }
                            else
                            {
                                result.Append(str, lowercaseStart, i - lowercaseStart);
                            }
                            lowercaseStart = i;
                            hasLowerCase = true;
                        }
                        else if (!IsWordSeparator(charType))
                        {
                            // This category is considered to be part of the word.
                            // This is any category that is marked as false in wordSeparator array.
                            i += charLen;
                        }
                        else
                        {
                            // A word separator. Break out of the loop.
                            break;
                        }
                    }

                    int count = i - lowercaseStart;

                    if (count > 0)
                    {
                        if (hasLowerCase)
                        {
                            lowercaseData ??= ToLower(str);
                            result.Append(lowercaseData, lowercaseStart, count);
                        }
                        else
                        {
                            result.Append(str, lowercaseStart, count);
                        }
                    }

                    if (i < str.Length)
                    {
                        // not a letter, just append it
                        i = AddNonLetter(ref result, ref str, i, charLen);
                    }
                }
                else
                {
                    // not a letter, just append it
                    i = AddNonLetter(ref result, ref str, i, charLen);
                }
            }
            return result.ToString();
        }

        // CharUnicodeInfo.GetUnicodeCategoryInternal: the category of the code point at index, and
        // whether it took a surrogate pair.
        private static UnicodeCategory GetUnicodeCategoryInternal(string value, int index, out int charLength)
        {
            if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                charLength = 2;
                return CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(value[index], value[index + 1]));
            }

            charLength = 1;
            return CharUnicodeInfo.GetUnicodeCategory(value[index]);
        }

        private static int AddNonLetter(ref StringBuilder result, ref string input, int inputIndex, int charLen)
        {
            Debug.Assert(charLen == 1 || charLen == 2, "[TextInfo.AddNonLetter] CharUnicodeInfo.InternalGetUnicodeCategory returned an unexpected charLen!");
            if (charLen == 2)
            {
                // Surrogate pair
                result.Append(input[inputIndex++]);
                result.Append(input[inputIndex]);
            }
            else
            {
                result.Append(input[inputIndex]);
            }
            return inputIndex;
        }

        private static int AddTitlecaseLetter(ref StringBuilder result, ref string input, int inputIndex, int charLen)
        {
            Debug.Assert(charLen == 1 || charLen == 2, "[TextInfo.AddTitlecaseLetter] CharUnicodeInfo.InternalGetUnicodeCategory returned an unexpected charLen!");

            if (charLen == 2)
            {
                // for surrogate pairs do a ToUpper operation on the substring
                result.Append(input.Substring(inputIndex, 2).ToUpperInvariant());
                inputIndex++;
            }
            else
            {
                switch (input[inputIndex])
                {
                    // For AppCompat, the Titlecase Case Mapping data from NDP 2.0 is used below.
                    case (char)0x01C4:  // DZ with Caron -> Dz with Caron
                    case (char)0x01C5:  // Dz with Caron -> Dz with Caron
                    case (char)0x01C6:  // dz with Caron -> Dz with Caron
                        result.Append((char)0x01C5);
                        break;
                    case (char)0x01C7:  // LJ -> Lj
                    case (char)0x01C8:  // Lj -> Lj
                    case (char)0x01C9:  // lj -> Lj
                        result.Append((char)0x01C8);
                        break;
                    case (char)0x01CA:  // NJ -> Nj
                    case (char)0x01CB:  // Nj -> Nj
                    case (char)0x01CC:  // nj -> Nj
                        result.Append((char)0x01CB);
                        break;
                    case (char)0x01F1:  // DZ -> Dz
                    case (char)0x01F2:  // Dz -> Dz
                    case (char)0x01F3:  // dz -> Dz
                        result.Append((char)0x01F2);
                        break;
                    default:
                        result.Append(char.ToUpperInvariant(input[inputIndex]));
                        break;
                }
            }
            return inputIndex;
        }

        // Used in ToTitleCase():
        // When we find a starting letter, the following array decides if a category should be
        // considered as word separator or not.
        private const int c_wordSeparatorMask =
            /* false */ (0 << 0) | // UppercaseLetter = 0,
            /* false */ (0 << 1) | // LowercaseLetter = 1,
            /* false */ (0 << 2) | // TitlecaseLetter = 2,
            /* false */ (0 << 3) | // ModifierLetter = 3,
            /* false */ (0 << 4) | // OtherLetter = 4,
            /* false */ (0 << 5) | // NonSpacingMark = 5,
            /* false */ (0 << 6) | // SpacingCombiningMark = 6,
            /* false */ (0 << 7) | // EnclosingMark = 7,
            /* false */ (0 << 8) | // DecimalDigitNumber = 8,
            /* false */ (0 << 9) | // LetterNumber = 9,
            /* false */ (0 << 10) | // OtherNumber = 10,
            /* true  */ (1 << 11) | // SpaceSeparator = 11,
            /* true  */ (1 << 12) | // LineSeparator = 12,
            /* true  */ (1 << 13) | // ParagraphSeparator = 13,
            /* true  */ (1 << 14) | // Control = 14,
            /* true  */ (1 << 15) | // Format = 15,
            /* false */ (0 << 16) | // Surrogate = 16,
            /* false */ (0 << 17) | // PrivateUse = 17,
            /* true  */ (1 << 18) | // ConnectorPunctuation = 18,
            /* true  */ (1 << 19) | // DashPunctuation = 19,
            /* true  */ (1 << 20) | // OpenPunctuation = 20,
            /* true  */ (1 << 21) | // ClosePunctuation = 21,
            /* true  */ (1 << 22) | // InitialQuotePunctuation = 22,
            /* true  */ (1 << 23) | // FinalQuotePunctuation = 23,
            /* true  */ (1 << 24) | // OtherPunctuation = 24,
            /* true  */ (1 << 25) | // MathSymbol = 25,
            /* true  */ (1 << 26) | // CurrencySymbol = 26,
            /* true  */ (1 << 27) | // ModifierSymbol = 27,
            /* true  */ (1 << 28) | // OtherSymbol = 28,
            /* false */ (0 << 29);  // OtherNotAssigned = 29;

        private static bool IsWordSeparator(UnicodeCategory category)
        {
            return (c_wordSeparatorMask & (1 << (int)category)) != 0;
        }

        private static bool IsLetterCategory(UnicodeCategory uc)
        {
            return uc == UnicodeCategory.UppercaseLetter
                 || uc == UnicodeCategory.LowercaseLetter
                 || uc == UnicodeCategory.TitlecaseLetter
                 || uc == UnicodeCategory.ModifierLetter
                 || uc == UnicodeCategory.OtherLetter;
        }
    }
}
