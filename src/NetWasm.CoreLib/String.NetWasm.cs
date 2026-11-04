// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's runtime part of string, as String.CoreCLR.cs is CoreCLR's; String.cs and its partials
// are upstream's. The compiler supplies FastAllocateString, Length, the indexer and
// GetRawStringData, so their bodies only stand for the lowering, and string literals are interned
// in a list. Normalization is not ported.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System
{
    public sealed partial class String
    {
        // The allocation every string comes from, as CoreCLR's FCall: the compiler allocates a
        // zeroed string of the length, and throws OverflowException past MaxLength.
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string FastAllocateString(int length);

        // The accessors of the Index and Range indexers NetWasm's string declared, which
        // upstream does not have, for released binaries that call them. As methods, no
        // element access binds to them, so `text[^1]` and `text[a..b]` lower to the int
        // indexer and Substring, as they do on .NET. Delete them with Span.NetWasm.cs.
        internal char get_Chars(Index index) => this[index.GetOffset(Length)];

        internal string get_Chars(Range range)
        {
            var (offset, length) = range.GetOffsetAndLength(Length);
            return Substring(offset, length);
        }

        // NetWasm's Create(char[]), which upstream does not have, for the released binaries that call
        // it (the 0.4.1 compatibility libraries' Regex). Delete it with the accessors above.
        internal static string Create(char[] characters)
        {
            ArgumentNullException.ThrowIfNull(characters);
            return new string(characters);
        }

        // Upstream Equals(a, b, StringComparison.OrdinalIgnoreCase) without the comparison switch, whose
        // culture-aware branches would make CultureInfo reachable from every case-insensitive dictionary.
        internal static bool EqualsOrdinalIgnoreCase(string? a, string? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null || a.Length != b.Length)
            {
                return false;
            }

            return EqualsOrdinalIgnoreCaseNoLengthCheck(a, b);
        }

        public static readonly string Empty = "";

        public int Length
        {
            get { return 0; }
        }

        [Runtime.CompilerServices.IndexerName("Chars")]
        public char this[int index]
        {
            get { return default(char); }
        }

        private static List<string>? s_internedStrings;

        internal ref char GetRawStringData() =>
            throw new PlatformNotSupportedException();

        public bool IsNormalized() => throw new PlatformNotSupportedException();

        public string Normalize() => throw new PlatformNotSupportedException();

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
    }
}
