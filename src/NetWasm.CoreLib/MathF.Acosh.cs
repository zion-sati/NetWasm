// Managed port of musl's acoshf.c. Upstream: musl v1.2.5, commit
// 0784374d561435f7c787a555aeab8ede699ed298. Adaptations: C# bit access; sqrtf,
// logf and log1pf are MathF's Sqrt, Log and Log1P.
/*
 * Copyright © 2005-2020 Rich Felker, et al.
 * SPDX-License-Identifier: MIT
 */

namespace System;

public static partial class MathF
{
    /* acosh(x) = log(x + sqrt(x*x-1)) */
    public static float Acosh(float x)
    {
        uint u = BitConverter.SingleToUInt32Bits(x);
        uint a = u & 0x7fffffff;

        if (a < 0x3f800000 + (1 << 23))
            /* |x| < 2, invalid if x < 1 */
            /* up to 2ulp error in [1,1.125] */
            return Log1P(x - 1 + Sqrt((x - 1) * (x - 1) + 2 * (x - 1)));
        if (u < 0x3f800000 + (12 << 23))
            /* 2 <= x < 0x1p12 */
            return Log(2 * x - 1 / (x + Sqrt(x * x - 1)));
        /* x >= 0x1p12 or x <= -2 or nan */
        return Log(x) + 0.693147180559945309417232121458176568f;
    }
}
