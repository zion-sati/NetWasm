// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The pi-scaled trigonometric functions from System.Private.CoreLib
// Single.cs at runtime commit 474ed963699fc29809015392738a15231aaae2de.

namespace System
{
    public partial struct Single
    {
        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.CosPi(TSelf)" />
        public static float CosPi(float x)
        {
            // This code is based on `cospif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (IsFinite(x))
            {
                float ax = Abs(x);

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;

                        float fractional = ax - integral;
                        float sign = int.IsOddInteger(integral) ? -1.0f : +1.0f;

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                result = sign * CosForIntervalPiBy4(fractional * Pi);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                result = sign * SinForIntervalPiBy4((0.5f - fractional) * Pi);
                            }
                            else
                            {
                                result = 0.0f;
                            }
                        }
                        else if (fractional <= 0.75)
                        {
                            result = -sign * SinForIntervalPiBy4((fractional - 0.5f) * Pi);
                        }
                        else
                        {
                            result = -sign * CosForIntervalPiBy4((1.0f - fractional) * Pi);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        result = CosForIntervalPiBy4(x * Pi);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        result = 1.0f - (value * value * 0.5f);
                    }
                    else
                    {
                        result = 1.0f;
                    }
                }
                else if (ax < 16_777_216.0f)        // |x| < 2^24
                {
                    // x is an integer
                    int bits = BitConverter.SingleToInt32Bits(ax);
                    result = int.IsOddInteger(bits) ? -1.0f : +1.0f;
                }
                else
                {
                    // x is an even integer
                    result = 1.0f;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.SinCosPi(TSelf)" />
        public static (float SinPi, float CosPi) SinCosPi(float x)
        {
            // This code is based on `cospif` and `sinpif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float sinPi;
            float cosPi;

            if (IsFinite(x))
            {
                float ax = Abs(x);

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;

                        float fractional = ax - integral;
                        float sign = int.IsOddInteger(integral) ? -1.0f : +1.0f;

                        float sinSign = ((x > 0.0f) ? +1.0f : -1.0f) * sign;
                        float cosSign = sign;

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                float value = fractional * Pi;

                                sinPi = sinSign * SinForIntervalPiBy4(value);
                                cosPi = cosSign * CosForIntervalPiBy4(value);
                            }
                            else
                            {
                                sinPi = x * 0.0f;
                                cosPi = cosSign;
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                float value = (0.5f - fractional) * Pi;

                                sinPi = sinSign * CosForIntervalPiBy4(value);
                                cosPi = cosSign * SinForIntervalPiBy4(value);
                            }
                            else
                            {
                                sinPi = sinSign;
                                cosPi = 0.0f;
                            }
                        }
                        else if (fractional <= 0.75f)
                        {
                            float value = (fractional - 0.5f) * Pi;

                            sinPi = +sinSign * CosForIntervalPiBy4(value);
                            cosPi = -cosSign * SinForIntervalPiBy4(value);
                        }
                        else
                        {
                            float value = (1.0f - fractional) * Pi;

                            sinPi = +sinSign * SinForIntervalPiBy4(value);
                            cosPi = -cosSign * CosForIntervalPiBy4(value);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        float value = x * Pi;

                        sinPi = SinForIntervalPiBy4(value);
                        cosPi = CosForIntervalPiBy4(value);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        float valueSq = value * value;

                        sinPi = value - (valueSq * value * (1.0f / 6.0f));
                        cosPi = 1.0f - (valueSq * 0.5f);
                    }
                    else
                    {
                        sinPi = x * Pi;
                        cosPi = 1.0f;
                    }
                }
                else if (ax < 16_777_216.0f)        // |x| < 2^24
                {
                    // x is an integer
                    sinPi = x * 0.0f;

                    int bits = BitConverter.SingleToInt32Bits(ax);
                    cosPi = int.IsOddInteger(bits) ? -1.0f : +1.0f;
                }
                else
                {
                    // x is an even integer
                    sinPi = x * 0.0f;
                    cosPi = 1.0f;
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
        public static float SinPi(float x)
        {
            // This code is based on `sinpif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (IsFinite(x))
            {
                float ax = Abs(x);

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;

                        float fractional = ax - integral;
                        float sign = ((x > 0.0f) ? +1.0f : -1.0f) * (int.IsOddInteger(integral) ? -1.0f : +1.0f);

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                result = sign * SinForIntervalPiBy4(fractional * Pi);
                            }
                            else
                            {
                                result = x * 0.0f;
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                result = sign * CosForIntervalPiBy4((0.5f - fractional) * Pi);
                            }
                            else
                            {
                                result = sign;
                            }
                        }
                        else if (fractional <= 0.75f)
                        {
                            result = sign * CosForIntervalPiBy4((fractional - 0.5f) * Pi);
                        }
                        else
                        {
                            result = sign * SinForIntervalPiBy4((1.0f - fractional) * Pi);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        result = SinForIntervalPiBy4(x * Pi);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        result = value - (value * value * value * (1.0f / 6.0f));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else
                {
                    // x is an integer
                    result = x * 0.0f;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.TanPi(TSelf)" />
        public static float TanPi(float x)
        {
            // This code is based on `tanpif` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            float result;

            if (IsFinite(x))
            {
                float ax = Abs(x);
                float sign = (x > 0.0f) ? +1.0f : -1.0f;

                if (ax < 8_388_608.0f)              // |x| < 2^23
                {
                    if (ax > 0.25f)
                    {
                        int integral = (int)ax;
                        float fractional = ax - integral;

                        if (fractional <= 0.25f)
                        {
                            if (fractional != 0.00f)
                            {
                                result = sign * TanForIntervalPiBy4(fractional * Pi, isReciprocal: false);
                            }
                            else
                            {
                                result = sign * (int.IsOddInteger(integral) ? -0.0f : +0.0f);
                            }
                        }
                        else if (fractional <= 0.50f)
                        {
                            if (fractional != 0.50f)
                            {
                                result = -sign * TanForIntervalPiBy4((0.5f - fractional) * Pi, isReciprocal: true);
                            }
                            else
                            {
                                result = +sign * (int.IsOddInteger(integral) ? NegativeInfinity : PositiveInfinity);
                            }
                        }
                        else if (fractional <= 0.75f)
                        {
                            result = +sign * TanForIntervalPiBy4((fractional - 0.5f) * Pi, isReciprocal: true);
                        }
                        else
                        {
                            result = -sign * TanForIntervalPiBy4((1.0f - fractional) * Pi, isReciprocal: false);
                        }
                    }
                    else if (ax >= 7.8125E-3f)      // |x| >= 2^-7
                    {
                        result = TanForIntervalPiBy4(x * Pi, isReciprocal: false);
                    }
                    else if (ax >= 1.22070313E-4f)  // |x| >= 2^-13
                    {
                        float value = x * Pi;
                        result = value + (value * value * value * (1.0f / 3.0f));
                    }
                    else
                    {
                        result = x * Pi;
                    }
                }
                else if (ax < 16_777_216)           // |x| < 2^24
                {
                    // x is an integer
                    int bits = BitConverter.SingleToInt32Bits(ax);
                    result = sign * (int.IsOddInteger(bits) ? -0.0f : +0.0f);
                }
                else
                {
                    // x is an even integer
                    result = sign * 0.0f;
                }
            }
            else
            {
                result = NaN;
            }

            return result;
        }

        private static float CosForIntervalPiBy4(float x)
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

            const double C1 = +0.41666666666666665390037E-1;        // approx: +1 / 4!
            const double C2 = -0.13888888888887398280412E-2;        // approx: -1 / 6!
            const double C3 = +0.248015872987670414957399E-4;       // approx: +1 / 8!
            const double C4 = -0.275573172723441909470836E-6;       // approx: -1 / 10!

            double xx = x * x;
            double result = C4;

            result = (result * xx) + C3;
            result = (result * xx) + C2;
            result = (result * xx) + C1;

            result *= xx * xx;
            result += 1.0 - (0.5 * xx);

            return (float)result;
        }

        private static float SinForIntervalPiBy4(float x)
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

            const double C1 = -0.166666666666666646259241729;       // approx: -1 / 3!
            const double C2 = +0.833333333333095043065222816E-2;    // approx: +1 / 5!
            const double C3 = -0.19841269836761125688538679E-3;     // approx: -1 / 7!
            const double C4 = +0.275573161037288022676895908448E-5; // approx: +1 / 9!

            double xx = x * x;
            double result = C4;

            result = (result * xx) + C3;
            result = (result * xx) + C2;
            result = (result * xx) + C1;

            result *= x * xx;
            result += x;

            return (float)result;
        }

        private static float TanForIntervalPiBy4(float x, bool isReciprocal)
        {
            // This code is based on `tan_piby4` from amd/aocl-libm-ose
            // Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
            //
            // Licensed under the BSD 3-Clause "New" or "Revised" License
            // See THIRD-PARTY-NOTICES.TXT for the full license text

            // Core Remez [1, 2] approximation to tan(x) on the interval [0, pi / 4].

            double xx = x * x;

            double denominator = +0.1844239256901656082986661E-1;
            denominator = -0.51396505478854532132342E+0 + (denominator * xx);
            denominator = +0.115588821434688393452299E+1 + (denominator * xx);

            double numerator = -0.172032480471481694693109E-1;
            numerator = 0.385296071263995406715129E+0 + (numerator * xx);

            double result = x * xx;
            result *= numerator / denominator;
            result += x;

            if (isReciprocal)
            {
                result = -1.0 / result;
            }

            return (float)result;
        }
    }
}
