// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

using System.Diagnostics;

namespace System.Security.Cryptography
{
    public partial struct ECCurve
    {
        /// <summary>
        /// Represents the type of elliptic curve.
        /// </summary>
        public enum ECCurveType : int
        {
            Implicit = 0,
            PrimeShortWeierstrass = 1,
            PrimeTwistedEdwards = 2,
            PrimeMontgomery = 3,
            Characteristic2 = 4,
            Named = 5,
        }
    }
}
