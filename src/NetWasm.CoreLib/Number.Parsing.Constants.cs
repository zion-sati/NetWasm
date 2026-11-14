// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Number.Parsing.cs): the floating-point
// limits Number.NumberToFloatingPointBits.cs uses. NetWasm's own parsing (Number.FloatParsing.cs) replaces the
// rest of that file.

namespace System
{
    internal static partial class Number
    {
        private const int FloatingPointMaxExponent = 309;
        private const int FloatingPointMinExponent = -324;

        private const int FloatingPointMaxDenormalMantissaBits = 52;
    }
}
