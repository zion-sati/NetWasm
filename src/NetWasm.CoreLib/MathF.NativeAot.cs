// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// src/coreclr/nativeaot/System.Private.CoreLib/src/System/MathF.NativeAot.cs.

/*============================================================
**
** Purpose: Some single-precision floating-point math operations
**
===========================================================*/

//This class contains only static members and doesn't require serialization.

using System.Runtime;
using System.Runtime.CompilerServices;

namespace System
{
    public static partial class MathF
    {
        [Intrinsic]
        public static float Acos(float x)
        {
            return RuntimeImports.acosf(x);
        }

        [Intrinsic]
        public static float Acosh(float x)
        {
            // NetWasm deviation (speed): musl's acoshf ported to C# (MathFPorts.Acosh.cs), which the compiler can
            // inline, in place of the C library's call; it gives musl's results bit for bit for all 2^32 inputs;
            // about 1.4 times as fast as the call in dotnet/performance's MathBenchmarks.
            return MathFPorts.Acosh(x);
        }

        [Intrinsic]
        public static float Asin(float x)
        {
            return RuntimeImports.asinf(x);
        }

        [Intrinsic]
        public static float Asinh(float x)
        {
            return RuntimeImports.asinhf(x);
        }

        [Intrinsic]
        public static float Atan(float x)
        {
            return RuntimeImports.atanf(x);
        }

        [Intrinsic]
        public static float Atan2(float y, float x)
        {
            return RuntimeImports.atan2f(y, x);
        }

        [Intrinsic]
        public static float Atanh(float x)
        {
            return RuntimeImports.atanhf(x);
        }

        [Intrinsic]
        public static float Cbrt(float x)
        {
            // NetWasm deviation (speed): musl's cbrtf ported to C# (MathFPorts.Cbrt.cs), which the compiler can
            // inline, in place of the C library's call; it gives musl's results bit for bit for all 2^32 inputs;
            // about 1.06 times as fast as the call in dotnet/performance's MathBenchmarks.
            return MathFPorts.Cbrt(x);
        }

        [Intrinsic]
        public static float Ceiling(float x)
        {
            return RuntimeImports.ceilf(x);
        }

        [Intrinsic]
        public static float Cos(float x)
        {
            return RuntimeImports.cosf(x);
        }

        [Intrinsic]
        public static float Cosh(float x)
        {
            return RuntimeImports.coshf(x);
        }

        [Intrinsic]
        public static float Exp(float x)
        {
            // NetWasm deviation (speed): musl's expf ported to C# (MathFPorts.ExpLog.cs), which the compiler can
            // inline, in place of the C library's call; it gives musl's results bit for bit for all 2^32 inputs;
            // about twice as fast as the call in dotnet/performance's MathBenchmarks.
            return MathFPorts.Exp(x);
        }

        [Intrinsic]
        public static float Floor(float x)
        {
            return RuntimeImports.floorf(x);
        }

        [Intrinsic]
        public static float FusedMultiplyAdd(float x, float y, float z)
        {
            return RuntimeImports.fmaf(x, y, z);
        }

        [Intrinsic]
        public static float Log(float x)
        {
            // NetWasm deviation (speed): musl's logf ported to C# (MathFPorts.ExpLog.cs), which the compiler can
            // inline, in place of the C library's call; it gives musl's results bit for bit for all 2^32 inputs;
            // about 1.6 times as fast as the call in dotnet/performance's MathBenchmarks.
            return MathFPorts.Log(x);
        }

        [Intrinsic]
        public static float Log2(float x)
        {
            // NetWasm deviation (speed): musl's log2f ported to C# (MathFPorts.ExpLog.cs), which the compiler can
            // inline, in place of the C library's call; it gives musl's results bit for bit for all 2^32 inputs;
            // about 1.8 times as fast as the call in dotnet/performance's MathBenchmarks.
            return MathFPorts.Log2(x);
        }

        [Intrinsic]
        public static float Log10(float x)
        {
            return RuntimeImports.log10f(x);
        }

        [Intrinsic]
        public static float Pow(float x, float y)
        {
            // NetWasm deviation (speed): musl's powf ported to C# (MathFPorts.ExpLog.cs) and inlined, so a constant
            // base or exponent folds the work that depends on it, in place of the C library's call; it gives musl's
            // results bit for bit on 100 million inputs; in dotnet/performance's MathBenchmarks, Single.Pow is about
            // 1.35 times as fast as the call and Single.Exp10, which is Pow(10, x), about 1.6 times.
            return MathFPorts.Pow(x, y);
        }

        [Intrinsic]
        public static float Sin(float x)
        {
            return RuntimeImports.sinf(x);
        }

        [Intrinsic]
        public static float Sinh(float x)
        {
            return RuntimeImports.sinhf(x);
        }

        [Intrinsic]
        public static float Sqrt(float x)
        {
            return RuntimeImports.sqrtf(x);
        }

        [Intrinsic]
        public static float Tan(float x)
        {
            return RuntimeImports.tanf(x);
        }

        [Intrinsic]
        public static float Tanh(float x)
        {
            return RuntimeImports.tanhf(x);
        }

        [Intrinsic]
        private static unsafe float ModF(float x, float* intptr)
        {
            return RuntimeImports.modff(x, intptr);
        }

        // NetWasm deviation (speed): one call to the C library's sincosf, which reduces the argument once for both,
        // in place of Sin and Cos; musl's sincosf uses sinf's and cosf's kernels and gives their results bit for bit
        // for all 2^32 inputs. CoreCLR also calls sincosf.
        // Inlined, so the caller keeps both results as values instead of a returned pair in its own value frame.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe (float Sin, float Cos) SinCos(float x)
        {
            // The results land in a static at a fixed address: a local's address would put both in a value frame
            // that every caller allocates. sincosf returns before anything else can use it.
            var results = (SinCosResults*)Unsafe.AsPointer(ref s_sinCosResults);
            RuntimeImports.sincosf(x, &results->Sin, &results->Cos);
            return (results->Sin, results->Cos);
        }

        private static SinCosResults s_sinCosResults;

        private struct SinCosResults
        {
            public float Sin;
            public float Cos;
        }
    }
}
