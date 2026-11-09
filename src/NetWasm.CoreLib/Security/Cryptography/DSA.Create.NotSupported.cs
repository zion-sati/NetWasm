// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

using System.Runtime.Versioning;

namespace System.Security.Cryptography
{
    public partial class DSA : AsymmetricAlgorithm
    {
        private static DSA CreateCore()
        {
            throw new PlatformNotSupportedException();
        }
    }
}
