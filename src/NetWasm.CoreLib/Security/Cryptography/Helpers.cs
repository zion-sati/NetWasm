// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de
// (Common/src/System/Security/Cryptography/Helpers.cs). NetWasm adaptation: only the members
// the ported cryptography sources use. MD5 is unavailable, as upstream reports for browser and wasi.
// HasSymmetricEncryption stays true: the symmetric algorithm classes exist and their browser
// NotSupported transforms throw PlatformNotSupportedException, so encrypted key formats reach that
// boundary before any output or key import (upstream browser and wasi throw CryptographicException).

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Internal.Cryptography
{
    internal static partial class Helpers
    {
        internal static bool HasSymmetricEncryption => true;

        public static bool IsRC2Supported => !OperatingSystem.IsAndroid() && !OperatingSystem.IsBrowser();

        internal static bool HasMD5 => false;

        [return: NotNullIfNotNull(nameof(src))]
        public static byte[]? CloneByteArray(this byte[]? src)
        {
            return src switch
            {
                null => null,
                { Length: 0 } => src,
                _ => (byte[])src.Clone(),
            };
        }

        internal static void RngFill(Span<byte> destination)
        {
            RandomNumberGenerator.Fill(destination);
        }

        internal static bool TryCopyToDestination(this ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
            if (source.TryCopyTo(destination))
            {
                bytesWritten = source.Length;
                return true;
            }

            bytesWritten = 0;
            return false;
        }
    }
}
