// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Runtime.Versioning;

namespace System.Security.Cryptography
{
    public partial class PasswordDeriveBytes : DeriveBytes
    {
        [SupportedOSPlatform("windows")]
        public byte[] CryptDeriveKey(string? algname, string? alghashname, int keySize, byte[] rgbIV)
        {
            throw new PlatformNotSupportedException(SR.Format(SR.Cryptography_CAPI_Required, nameof(CryptDeriveKey)));
        }
    }
}
