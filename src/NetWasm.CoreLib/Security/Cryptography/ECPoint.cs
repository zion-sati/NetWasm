// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

namespace System.Security.Cryptography
{
    /// <summary>
    /// Represents a point on an elliptic curve.
    /// </summary>
    public struct ECPoint
    {
        public byte[]? X;
        public byte[]? Y;
    }
}
