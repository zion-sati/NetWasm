// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net.Mime
{
    public enum TransferEncoding
    {
        Unknown = -1,
        QuotedPrintable = 0,
        Base64 = 1,
        SevenBit = 2,
        EightBit = 3,
    }
}
