// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.IO.Compression.Brotli uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.IO.Compression.Brotli's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string ArgumentOutOfRange_Enum = "Enum value was out of legal range.";
        internal const string InvalidBeginCall = "Only one asynchronous reader or writer is allowed time at one time.";
        internal const string Stream_FalseCanRead = "Stream does not support reading.";
        internal const string Stream_FalseCanWrite = "Stream does not support writing.";
        internal const string BrotliDecoder_Create = "Failed to create BrotliDecoder instance";
        internal const string BrotliDecoder_Disposed = "Can not access a closed Decoder.";
        internal const string BrotliStream_Compress_UnsupportedOperation = "Can not perform Read operations on a BrotliStream constructed with CompressionMode.Compress.";
        internal const string BrotliStream_Decompress_InvalidData = "Decoder ran into invalid data.";
        internal const string BrotliStream_Decompress_InvalidStream = "BrotliStream.BaseStream returned more bytes than requested in Read.";
        internal const string BrotliStream_Decompress_TruncatedData = "Found truncated data while decoding.";
        internal const string BrotliEncoder_Create = "Failed to create BrotliEncoder instance";
        internal const string BrotliEncoder_Disposed = "Can not access a closed Encoder.";
        internal const string BrotliEncoder_InvalidSetParameter = "The BrotliEncoder {0} can not be changed at current encoder state.";
        internal const string BrotliEncoder_Quality = "Provided BrotliEncoder Quality of {0} is not between the minimum value of {1} and the maximum value of {2}";
        internal const string BrotliEncoder_Window = "Provided BrotliEncoder Window of {0} is not between the minimum value of {1} and the maximum value of {2}";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string BrotliStream_Compress_InvalidData = "Encoder ran into invalid data.";
        internal const string BrotliStream_Decompress_UnsupportedOperation = "Can not perform Write operations on a BrotliStream constructed with CompressionMode.Decompress.";
    }
}
