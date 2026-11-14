// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Resources.Writer uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Resources.Writer's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string ArgumentOutOfRange_StreamLength = "Stream length must be non-negative and less than 2^31 - 1 - origin.";
        internal const string Argument_StreamNotWritable = "Stream was not writable.";
        internal const string InvalidOperation_ResourceWriterSaved = "The resource writer has already been closed and cannot be edited.";
        internal const string NotSupported_BinarySerializedResources = "This platform does not support binary serialized resources.";
        internal const string NotSupported_UnseekableStream = "Stream does not support seeking.";
        internal const string PlatformNotSupported_ResourceFiles = "NetWasm reads and writes .resources data through a caller-supplied stream, not a file name.";
    }
}
