// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Hypot and RootN from System.Private.CoreLib Single.cs at runtime commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Diagnostics;

namespace System
{
    public partial struct Single
    {
        /// <inheritdoc cref="IRootFunctions{TSelf}.Hypot(TSelf, TSelf)" />
        public static float Hypot(float x, float y)
        {
            // This code is based on `hypotf` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (IsFinite(x) && IsFinite(y))
            {
                float ax = Abs(x);
                float ay = Abs(y);

                if (ax == 0.0f)
                {
                    result = ay;
                }
                else if (ay == 0.0f)
                {
                    result = ax;
                }
                else
                {
                    double xx = ax;
                    xx *= xx;

                    double yy = ay;
                    yy *= yy;

                    result = (float)double.Sqrt(xx + yy);
                }
            }
            else if (IsInfinity(x) || IsInfinity(y))
            {
                // IEEE 754 requires that we return +Infinity
                // even if one of the inputs is NaN

                result = PositiveInfinity;
            }
            else
            {
                // IEEE 754 requires that we return NaN
                // if either input is NaN and neither is Infinity

                Debug.Assert(IsNaN(x) || IsNaN(y));
                result = NaN;
            }

            return result;
        }

        /// <inheritdoc cref="IRootFunctions{TSelf}.RootN(TSelf, int)" />
        public static float RootN(float x, int n)
        {
            float result;

            if (n > 0)
            {
                if (n == 2)
                {
                    result = (x != 0.0f) ? Sqrt(x) : 0.0f;
                }
                else if (n == 3)
                {
                    result = Cbrt(x);
                }
                else
                {
                    result = PositiveN(x, n);
                }
            }
            else if (n < 0)
            {
                result = NegativeN(x, n);
            }
            else
            {
                Debug.Assert(n == 0);
                result = NaN;
            }

            return result;

            static float PositiveN(float x, int n)
            {
                float result;

                if (IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = (float)double.Pow(Abs(x), 1.0 / n);
                            result = CopySign(result, x);
                        }
                        else
                        {
                            result = NaN;
                        }
                    }
                    else if (IsEvenInteger(n))
                    {
                        result = 0.0f;
                    }
                    else
                    {
                        result = CopySign(0.0f, x);
                    }
                }
                else if (IsNaN(x))
                {
                    result = NaN;
                }
                else if (x > 0)
                {
                    Debug.Assert(IsPositiveInfinity(x));
                    result = PositiveInfinity;
                }
                else
                {
                    Debug.Assert(IsNegativeInfinity(x));
                    result = int.IsOddInteger(n) ? NegativeInfinity : NaN;
                }

                return result;
            }

            static float NegativeN(float x, int n)
            {
                float result;

                if (IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = (float)double.Pow(Abs(x), 1.0 / n);
                            result = CopySign(result, x);
                        }
                        else
                        {
                            result = NaN;
                        }
                    }
                    else if (IsEvenInteger(n))
                    {
                        result = PositiveInfinity;
                    }
                    else
                    {
                        result = CopySign(PositiveInfinity, x);
                    }
                }
                else if (IsNaN(x))
                {
                    result = NaN;
                }
                else if (x > 0)
                {
                    Debug.Assert(IsPositiveInfinity(x));
                    result = 0.0f;
                }
                else
                {
                    Debug.Assert(IsNegativeInfinity(x));
                    result = int.IsOddInteger(n) ? -0.0f : NaN;
                }

                return result;
            }
        }
    }
}
