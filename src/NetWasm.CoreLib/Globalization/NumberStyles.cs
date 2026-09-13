// Adapted from dotnet/runtime System.Private.CoreLib NumberStyles.cs.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
namespace System.Globalization
{
    [Flags]
    public enum NumberStyles
    {
        None = 0,
        AllowLeadingWhite = 1,
        AllowTrailingWhite = 2,
        AllowLeadingSign = 4,
        AllowTrailingSign = 8,
        AllowParentheses = 16,
        AllowDecimalPoint = 32,
        AllowThousands = 64,
        AllowExponent = 128,
        AllowCurrencySymbol = 256,
        AllowHexSpecifier = 512,
        AllowBinarySpecifier = 1024,
        Integer = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign,
        HexNumber = AllowLeadingWhite | AllowTrailingWhite | AllowHexSpecifier,
        BinaryNumber = AllowLeadingWhite | AllowTrailingWhite | AllowBinarySpecifier,
        Number = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign |
            AllowTrailingSign | AllowDecimalPoint | AllowThousands,
        Float = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign |
            AllowDecimalPoint | AllowExponent,
        HexFloat = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign |
            AllowDecimalPoint | AllowExponent | AllowHexSpecifier,
        Currency = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign |
            AllowTrailingSign | AllowParentheses | AllowDecimalPoint |
            AllowThousands | AllowCurrencySymbol,
        Any = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign |
            AllowTrailingSign | AllowParentheses | AllowDecimalPoint |
            AllowThousands | AllowCurrencySymbol | AllowExponent,
    }
}
