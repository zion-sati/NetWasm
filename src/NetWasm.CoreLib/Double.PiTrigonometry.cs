// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The pi-scaled trigonometric functions from System.Private.CoreLib
// Double.cs at runtime commit 474ed963699fc29809015392738a15231aaae2de.

namespace System
{
    public partial struct Double
    {
        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.CosPi(TSelf)" />
        public static double CosPi(double x)
        {
            // This code is based on `cospi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (IsFinite(x))
            {
                double ax = Abs(x);

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;

                        double fractional = ax - integral;
                        double sign = long.IsOddInteger(integral) ? -1.0 : +1.0;

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                result = sign * CosForIntervalPiBy4(fractional * Pi, 0.0);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                result = sign * SinForIntervalPiBy4((0.5 - fractional) * Pi, 0.0);
                            }
                            else
                            {
                                result = 0.0;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = -sign * SinForIntervalPiBy4((fractional - 0.5) * Pi, 0.0);
                        }
                        else
                        {
                            result = -sign * CosForIntervalPiBy4((1.0 - fractional) * Pi, 0.0);
                        }
                    }
                    else if (ax >= 6.103515625E-05)         // |x| >= 2^-14
                    {
                        result = CosForIntervalPiBy4(x * Pi, 0.0);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        result = 1.0 - (value * value * 0.5);
                    }
                    else
                    {
                        result = 1.0;
                    }
                }
                else if (ax < 9_007_199_254_740_992.0)      // |x| < 2^53
                {
                    // x is an integer
                    long bits = BitConverter.DoubleToInt64Bits(ax);
                    result = long.IsOddInteger(bits) ? -1.0 : +1.0;
                }
                else
                {
                    // x is an even integer
                    result = 1.0;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.SinCosPi(TSelf)" />
        public static (double SinPi, double CosPi) SinCosPi(double x)
        {
            // This code is based on `cospi` and `sinpi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double sinPi;
            double cosPi;

            if (IsFinite(x))
            {
                double ax = Abs(x);

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;

                        double fractional = ax - integral;
                        double sign = long.IsOddInteger(integral) ? -1.0 : +1.0;

                        double sinSign = ((x > 0.0) ? +1.0 : -1.0) * sign;
                        double cosSign = sign;

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                double value = fractional * Pi;

                                sinPi = sinSign * SinForIntervalPiBy4(value, 0.0);
                                cosPi = cosSign * CosForIntervalPiBy4(value, 0.0);
                            }
                            else
                            {
                                sinPi = x * 0.0;
                                cosPi = cosSign;
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                double value = (0.5 - fractional) * Pi;

                                sinPi = sinSign * CosForIntervalPiBy4(value, 0.0);
                                cosPi = cosSign * SinForIntervalPiBy4(value, 0.0);
                            }
                            else
                            {
                                sinPi = sinSign;
                                cosPi = 0.0;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            double value = (fractional - 0.5) * Pi;

                            sinPi = +sinSign * CosForIntervalPiBy4(value, 0.0);
                            cosPi = -cosSign * SinForIntervalPiBy4(value, 0.0);
                        }
                        else
                        {
                            double value = (1.0 - fractional) * Pi;

                            sinPi = +sinSign * SinForIntervalPiBy4(value, 0.0);
                            cosPi = -cosSign * CosForIntervalPiBy4(value, 0.0);
                        }
                    }
                    else if (ax >= 1.220703125E-4)          // |x| >= 2^-13
                    {
                        double value = x * Pi;

                        sinPi = SinForIntervalPiBy4(value, 0.0);
                        cosPi = CosForIntervalPiBy4(value, 0.0);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        double valueSq = value * value;

                        sinPi = value - (valueSq * value * (1.0 / 6.0));
                        cosPi = 1.0 - (valueSq * 0.5);
                    }
                    else
                    {
                        sinPi = x * Pi;
                        cosPi = 1.0;
                    }
                }
                else if (ax < 9_007_199_254_740_992.0)      // |x| < 2^53
                {
                    // x is an integer
                    sinPi = x * 0.0;

                    long bits = BitConverter.DoubleToInt64Bits(ax);
                    cosPi = long.IsOddInteger(bits) ? -1.0 : +1.0;
                }
                else
                {
                    // x is an even integer
                    sinPi = x * 0.0;
                    cosPi = 1.0;
                }
            }
            else
            {
                sinPi = NaN;
                cosPi = NaN;
            }

            return (sinPi, cosPi);
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.SinPi(TSelf)" />
        public static double SinPi(double x)
        {
            // This code is based on `sinpi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (IsFinite(x))
            {
                double ax = Abs(x);

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;

                        double fractional = ax - integral;
                        double sign = ((x > 0.0) ? +1.0 : -1.0) * (long.IsOddInteger(integral) ? -1.0 : +1.0);

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                result = sign * SinForIntervalPiBy4(fractional * Pi, 0.0);
                            }
                            else
                            {
                                result = x * 0.0;
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                result = sign * CosForIntervalPiBy4((0.5 - fractional) * Pi, 0.0);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = sign * CosForIntervalPiBy4((fractional - 0.5) * Pi, 0.0);
                        }
                        else
                        {
                            result = sign * SinForIntervalPiBy4((1.0 - fractional) * Pi, 0.0);
                        }
                    }
                    else if (ax >= 1.220703125E-4)          // |x| >= 2^-13
                    {
                        result = SinForIntervalPiBy4(x * Pi, 0.0);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        result = value - (value * value * value * (1.0 / 6.0));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else
                {
                    // x is an integer
                    result = x * 0.0;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.TanPi(TSelf)" />
        public static double TanPi(double x)
        {
            // This code is based on `tanpi` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            double result;

            if (IsFinite(x))
            {
                double ax = Abs(x);
                double sign = (x > 0.0) ? +1.0 : -1.0;

                if (ax < 4_503_599_627_370_496.0)           // |x| < 2^52
                {
                    if (ax > 0.25)
                    {
                        long integral = (long)ax;
                        double fractional = ax - integral;

                        if (fractional <= 0.25)
                        {
                            if (fractional != 0.00)
                            {
                                result = sign * TanForIntervalPiBy4(fractional * Pi, 0.0, isReciprocal: false);
                            }
                            else
                            {
                                result = sign * (long.IsOddInteger(integral) ? -0.0 : +0.0);
                            }
                        }
                        else if (fractional <= 0.50)
                        {
                            if (fractional != 0.50)
                            {
                                result = -sign * TanForIntervalPiBy4((0.5 - fractional) * Pi, 0.0, isReciprocal: true);
                            }
                            else
                            {
                                result = +sign * (long.IsOddInteger(integral) ? NegativeInfinity : PositiveInfinity);
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = +sign * TanForIntervalPiBy4((fractional - 0.5) * Pi, 0.0, isReciprocal: true);
                        }
                        else
                        {
                            result = -sign * TanForIntervalPiBy4((1.0 - fractional) * Pi, 0.0, isReciprocal: false);
                        }
                    }
                    else if (ax >= 6.103515625E-05)         // |x| >= 2^-14
                    {
                        result = TanForIntervalPiBy4(x * Pi, 0.0, isReciprocal: false);
                    }
                    else if (ax >= 7.450580596923828E-09)   // |x| >= 2^-27
                    {
                        double value = x * Pi;
                        result = value + (value * value * value * (1.0 / 3.0));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else if (ax < 9_007_199_254_740_992.0)      // |x| < 2^53
                {
                    // x is an integer
                    long bits = BitConverter.DoubleToInt64Bits(ax);
                    result = sign * (long.IsOddInteger(bits) ? -0.0 : +0.0);
                }
                else
                {
                    // x is an even integer
                    result = sign * 0.0;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        private static double CosForIntervalPiBy4(double x, double xTail)
        {
            // This code is based on `cos_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Taylor series for cos(x) is: 1 - (x^2 / 2!) + (x^4 / 4!) - (x^6 / 6!) ...
            //
            // Then define f(xx) where xx = (x * x)
            // and f(xx) = 1 - (xx / 2!) + (xx^2 / 4!) - (xx^3 / 6!) ...
            //
            // We use a minimax approximation of (f(xx) - 1 + (xx / 2)) / (xx * xx)
            // because this produces an expansion in even powers of x.
            //
            // If xTail is non-zero, we subtract a correction term g(x, xTail) = (x * xTail)
            // to the result, where g(x, xTail) is an approximation to sin(x) * sin(xTail)
            //
            // This is valid because xTail is tiny relative to x.

            const double C1 = +0.41666666666666665390037E-1;        // approx: +1 / 4!
            const double C2 = -0.13888888888887398280412E-2;        // approx: -1 / 6!
            const double C3 = +0.248015872987670414957399E-4;       // approx: +1 / 8!
            const double C4 = -0.275573172723441909470836E-6;       // approx: -1 / 10!
            const double C5 = +0.208761463822329611076335E-8;       // approx: +1 / 12!
            const double C6 = -0.113826398067944859590880E-10;      // approx: -1 / 14!

            double xx = x * x;

            double tmp1 = 0.5 * xx;
            double tmp2 = 1.0 - tmp1;

            double result = C6;

            result = (result * xx) + C5;
            result = (result * xx) + C4;
            result = (result * xx) + C3;
            result = (result * xx) + C2;
            result = (result * xx) + C1;

            result *= (xx * xx);
            result += 1.0 - tmp2 - tmp1 - (x * xTail);
            result += tmp2;

            return result;
        }

        private static double SinForIntervalPiBy4(double x, double xTail)
        {
            // This code is based on `sin_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Taylor series for sin(x) is x - (x^3 / 3!) + (x^5 / 5!) - (x^7 / 7!) ...
            // Which can be expressed as x * (1 - (x^2 / 3!) + (x^4 /5!) - (x^6 /7!) ...)
            //
            // Then define f(xx) where xx = (x * x)
            // and f(xx) = 1 - (xx / 3!) + (xx^2 / 5!) - (xx^3 / 7!) ...
            //
            // We use a minimax approximation of (f(xx) - 1) / xx
            // because this produces an expansion in even powers of x.
            //
            // If xTail is non-zero, we add a correction term g(x, xTail) = (1 - xx / 2) * xTail
            // to the result, where g(x, xTail) is an approximation to cos(x) * sin(xTail)
            //
            // This is valid because xTail is tiny relative to x.

            const double C1 = -0.166666666666666646259241729;       // approx: -1 / 3!
            const double C2 = +0.833333333333095043065222816E-2;    // approx: +1 / 5!
            const double C3 = -0.19841269836761125688538679E-3;     // approx: -1 / 7!
            const double C4 = +0.275573161037288022676895908448E-5; // approx: +1 / 9!
            const double C5 = -0.25051132068021699772257377197E-7;  // approx: -1 / 11!
            const double C6 = +0.159181443044859136852668200E-9;    // approx: +1 / 13!

            double xx = x * x;
            double xxx = xx * x;

            double result = C6;

            result = (result * xx) + C5;
            result = (result * xx) + C4;
            result = (result * xx) + C3;
            result = (result * xx) + C2;

            if (xTail == 0.0)
            {
                result = (xx * result) + C1;
                result = (xxx * result) + x;
            }
            else
            {
                result = x - ((xx * ((0.5 * xTail) - (xxx * result))) - xTail - (xxx * C1));
            }

            return result;
        }

        private static double TanForIntervalPiBy4(double x, double xTail, bool isReciprocal)
        {
            // This code is based on `tan_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // In order to maintain relative precision transform using the identity:
            //  tan((pi / 4) - x) = (1 - tan(x)) / (1 + tan(x)) for arguments close to (pi / 4).
            //
            // Similarly use tan(x - (pi / 4)) = (tan(x) - 1) / (tan(x) + 1) close to (-pi / 4).

            const double PiBy4Head = 7.85398163397448278999E-01;
            const double PiBy4Tail = 3.06161699786838240164E-17;

            int transform = 0;

            if (x > +0.68)
            {
                transform = 1;
                x = (PiBy4Head - x) + (PiBy4Tail - xTail);
                xTail = 0.0;
            }
            else if (x < -0.68)
            {
                transform = -1;
                x = (PiBy4Head + x) + (PiBy4Tail + xTail);
                xTail = 0.0;
            }

            // Core Remez [2, 3] approximation to tan(x + xTail) on the interval [0, 0.68].

            double tmp1 = (x * x) + (2.0 * x * xTail);

            double denominator = -0.232371494088563558304549252913E-3;
            denominator = +0.260656620398645407524064091208E-1 + (denominator * tmp1);
            denominator = -0.515658515729031149329237816945E+0 + (denominator * tmp1);
            denominator = +0.111713747927937668539901657944E+1 + (denominator * tmp1);

            double numerator = +0.224044448537022097264602535574E-3;
            numerator = -0.229345080057565662883358588111E-1 + (numerator * tmp1);
            numerator = +0.372379159759792203640806338901E+0 + (numerator * tmp1);

            double tmp2 = x * tmp1;
            tmp2 *= numerator / denominator;
            tmp2 += xTail;

            // Reconstruct tan(x) in the transformed case

            double result = x + tmp2;

            if (transform != 0)
            {
                if (isReciprocal)
                {
                    result = (transform * (2 * result / (result - 1))) - 1.0;
                }
                else
                {
                    result = transform * (1.0 - (2 * result / (1 + result)));
                }
            }
            else if (isReciprocal)
            {
                // Compute -1.0 / (x + tmp2) accurately

                ulong bits = BitConverter.DoubleToUInt64Bits(result);
                bits &= 0xFFFFFFFF00000000;

                double z1 = BitConverter.UInt64BitsToDouble(bits);
                double z2 = tmp2 - (z1 - x);

                double reciprocal = -1.0 / result;

                bits = BitConverter.DoubleToUInt64Bits(reciprocal);
                bits &= 0xFFFFFFFF00000000;

                double reciprocalHead = BitConverter.UInt64BitsToDouble(bits);
                result = reciprocalHead + (reciprocal * (1.0 + (reciprocalHead * z1) + (reciprocalHead * z2)));
            }

            return result;
        }
    }
}
