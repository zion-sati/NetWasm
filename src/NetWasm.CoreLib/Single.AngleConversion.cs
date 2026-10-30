// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// DegreesToRadians and RadiansToDegrees from System.Private.CoreLib Single.cs at runtime commit
// 474ed963699fc29809015392738a15231aaae2de.

namespace System
{
    public partial struct Single
    {
        // Multiplying by `head` alone is enough here, without the rest of the triple that `double`
        // needs: the constant being inexact is under `2^-54.6` relative and the single rounding of
        // the product is `2^-53`, together under `2^-28.6` of a `float` ulp, while no significand
        // brings the exact value nearer than `2^-26.5` ulp to the boundary between two results
        // (`2^-26.470` for `DegreesToRadians`, `2^-24.5` for `RadiansToDegrees`).

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.DegreesToRadians(TSelf)" />
        public static float DegreesToRadians(float degrees) => (float)(degrees * double.DegreesToRadiansHead);

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.RadiansToDegrees(TSelf)" />
        public static float RadiansToDegrees(float radians) => (float)(radians * double.RadiansToDegreesHead);
    }
}
