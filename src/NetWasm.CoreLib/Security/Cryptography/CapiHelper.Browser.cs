// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

namespace System.Security.Cryptography
{
    internal static partial class CapiHelper
    {
        // Return PROV_RSA_AES, in case any compat case pops up.
        internal const int DefaultRsaProviderType = 24;
    }
}
