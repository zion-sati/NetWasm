// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Hypot and RootN from System.Private.CoreLib Double.cs at runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;

namespace System
{
    public readonly partial struct Double
    {
        /// <inheritdoc cref="IRootFunctions{TSelf}.Hypot(TSelf, TSelf)" />
        public static double Hypot(double x, double y)
        {
            // This code is based on `hypot` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (IsFinite(x) && IsFinite(y))
            {
                double ax = Abs(x);
                double ay = Abs(y);

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
                    ulong xBits = BitConverter.DoubleToUInt64Bits(ax);
                    ulong yBits = BitConverter.DoubleToUInt64Bits(ay);

                    uint xExp = (uint)((xBits >> BiasedExponentShift) & ShiftedBiasedExponentMask);
                    uint yExp = (uint)((yBits >> BiasedExponentShift) & ShiftedBiasedExponentMask);

                    int expDiff = (int)(xExp - yExp);
                    double expFix = 1.0;

                    if ((expDiff <= (SignificandLength + 1)) && (expDiff >= (-SignificandLength - 1)))
                    {
                        if ((xExp > (ExponentBias + 500)) || (yExp > (ExponentBias + 500)))
                        {
                            // To prevent overflow, scale down by 2^+600
                            expFix = 4.149515568880993E+180;

                            xBits -= 0x2580000000000000;
                            yBits -= 0x2580000000000000;
                        }
                        else if ((xExp < (ExponentBias - 500)) || (yExp < (ExponentBias - 500)))
                        {
                            // To prevent underflow, scale up by 2^-600
                            expFix = 2.409919865102884E-181;

                            xBits += 0x2580000000000000;
                            yBits += 0x2580000000000000;

                            // For subnormal values, do an additional fixing up changing the
                            // adjustment to scale up by 2^601 instead and then subtract a
                            // correction of 2^601 to account for the implicit bit.

                            if (xExp == 0) // x is subnormal
                            {
                                xBits += 0x0010000000000000;

                                ax = BitConverter.UInt64BitsToDouble(xBits);
                                ax -= 9.232978617785736E-128;

                                xBits = BitConverter.DoubleToUInt64Bits(ax);
                            }

                            if (yExp == 0) // y is subnormal
                            {
                                yBits += 0x0010000000000000;

                                ay = BitConverter.UInt64BitsToDouble(yBits);
                                ay -= 9.232978617785736E-128;

                                yBits = BitConverter.DoubleToUInt64Bits(ay);
                            }
                        }

                        ax = BitConverter.UInt64BitsToDouble(xBits);
                        ay = BitConverter.UInt64BitsToDouble(yBits);

                        if (ax < ay)
                        {
                            // Sort so ax is greater than ay
                            double tmp = ax;

                            ax = ay;
                            ay = tmp;

                            ulong tmpBits = xBits;

                            xBits = yBits;
                            yBits = tmpBits;
                        }

                        Debug.Assert(ax >= ay);

                        // Split ax and ay into a head and tail portion

                        double xHead = BitConverter.UInt64BitsToDouble(xBits & 0xFFFF_FFFF_F800_0000);
                        double yHead = BitConverter.UInt64BitsToDouble(yBits & 0xFFFF_FFFF_F800_0000);

                        double xTail = ax - xHead;
                        double yTail = ay - yHead;

                        // Compute (x * x) + (y * y) with extra precision
                        //
                        // This includes taking into account expFix which may
                        // cause an underflow or overflow, but if it does that
                        // will still be the correct result.

                        double xx = ax * ax;
                        double yy = ay * ay;

                        double rHead = xx + yy;
                        double rTail = (xx - rHead) + yy;

                        rTail += (xHead * xHead) - xx;
                        rTail += 2 * xHead * xTail;
                        rTail += xTail * xTail;

                        if (expDiff == 0)
                        {
                            // We only need to do extra accounting when ax and ay have equal exponents

                            rTail += (yHead * yHead) - yy;
                            rTail += 2 * yHead * yTail;
                            rTail += yTail * yTail;
                        }

                        result = Sqrt(rHead + rTail) * expFix;
                    }
                    else
                    {
                        // x or y is insignificant compared to the other
                        result = ax + ay;
                    }
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

                result = NaN;
            }

            return result;
        }

        /// <inheritdoc cref="IRootFunctions{TSelf}.RootN(TSelf, int)" />
        public static double RootN(double x, int n)
        {
            double result;

            if (n > 0)
            {
                if (n == 2)
                {
                    result = (x != 0.0) ? Sqrt(x) : 0.0;
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

            static double PositiveN(double x, int n)
            {
                double result;

                if (IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = Pow(Abs(x), 1.0 / n);
                            result = CopySign(result, x);
                        }
                        else
                        {
                            result = NaN;
                        }
                    }
                    else if (IsEvenInteger(n))
                    {
                        result = 0.0;
                    }
                    else
                    {
                        result = CopySign(0.0, x);
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

            static double NegativeN(double x, int n)
            {
                double result;

                if (IsFinite(x))
                {
                    if (x != 0)
                    {
                        if ((x > 0) || IsOddInteger(n))
                        {
                            result = Pow(Abs(x), 1.0 / n);
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
                    result = 0.0;
                }
                else
                {
                    Debug.Assert(IsNegativeInfinity(x));
                    result = int.IsOddInteger(n) ? -0.0 : NaN;
                }

                return result;
            }
        }
    }
}
