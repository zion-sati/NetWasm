// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

namespace System.Security.Cryptography
{
    public partial class ECDiffieHellman : ECAlgorithm
    {
        public static new partial ECDiffieHellman Create()
        {
            throw new PlatformNotSupportedException();
        }

        public static partial ECDiffieHellman Create(ECCurve curve)
        {
            throw new PlatformNotSupportedException();
        }

        public static partial ECDiffieHellman Create(ECParameters parameters)
        {
            throw new PlatformNotSupportedException();
        }
    }
}
