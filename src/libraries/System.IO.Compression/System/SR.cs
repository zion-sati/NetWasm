// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.IO.Compression uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.IO.Compression's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string IO_NotSupported_UnreadableStream = "Stream does not support reading.";
        internal const string IO_NotSupported_UnseekableStream = "Stream does not support seeking.";
        internal const string IO_NotSupported_UnwritableStream = "Stream does not support writing.";
        internal const string IO_SeekBeforeBegin = "An attempt was made to move the position before the beginning of the stream.";
    }
}
