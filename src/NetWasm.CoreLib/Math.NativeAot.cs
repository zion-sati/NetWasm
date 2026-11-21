// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// src/coreclr/nativeaot/System.Private.CoreLib/src/System/Math.NativeAot.cs.

/*============================================================
**
** Purpose: Some floating-point math operations
**
===========================================================*/

//This class contains only static members and doesn't require serialization.

using System.Runtime;
using System.Runtime.CompilerServices;

namespace System
{
    public static partial class Math
    {
        [Intrinsic]
        public static double Acos(double d)
        {
            // NetWasm deviation (speed): fdlibm's acos ported to C# (MathPorts.Acos.cs), which the compiler can
            // inline, in place of the C library's call; musl's acos is fdlibm's, and the port gives its results bit
            // for bit on 50 million inputs; about 1.15 times as fast as the call in dotnet/performance's
            // MathBenchmarks.
            return MathPorts.Acos(d);
        }

        [Intrinsic]
        public static double Acosh(double d)
        {
            return RuntimeImports.acosh(d);
        }

        [Intrinsic]
        public static double Asin(double d)
        {
            // NetWasm deviation (speed): fdlibm's asin ported to C# (MathPorts.Asin.cs), which the compiler can
            // inline, in place of the C library's call; musl's asin is fdlibm's, and the port gives its results bit
            // for bit on 50 million inputs; about 1.2 times as fast as the call in dotnet/performance's
            // MathBenchmarks.
            return MathPorts.Asin(d);
        }

        [Intrinsic]
        public static double Asinh(double d)
        {
            return RuntimeImports.asinh(d);
        }

        [Intrinsic]
        public static double Atan(double d)
        {
            return RuntimeImports.atan(d);
        }

        [Intrinsic]
        public static double Atan2(double y, double x)
        {
            return RuntimeImports.atan2(y, x);
        }

        [Intrinsic]
        public static double Atanh(double d)
        {
            return RuntimeImports.atanh(d);
        }

        [Intrinsic]
        public static double Cbrt(double d)
        {
            return RuntimeImports.cbrt(d);
        }

        [Intrinsic]
        public static double Ceiling(double a)
        {
            return RuntimeImports.ceil(a);
        }

        [Intrinsic]
        public static double Cos(double d)
        {
            return RuntimeImports.cos(d);
        }

        [Intrinsic]
        public static double Cosh(double value)
        {
            return RuntimeImports.cosh(value);
        }

        [Intrinsic]
        public static double Exp(double d)
        {
            return RuntimeImports.exp(d);
        }

        [Intrinsic]
        public static double Floor(double d)
        {
            return RuntimeImports.floor(d);
        }

        [Intrinsic]
        public static double FusedMultiplyAdd(double x, double y, double z)
        {
            return RuntimeImports.fma(x, y, z);
        }

        [Intrinsic]
        public static double Log(double d)
        {
            return RuntimeImports.log(d);
        }

        [Intrinsic]
        public static double Log2(double x)
        {
            return RuntimeImports.log2(x);
        }

        [Intrinsic]
        public static double Log10(double d)
        {
            return RuntimeImports.log10(d);
        }

        [Intrinsic]
        public static double Pow(double x, double y)
        {
            // NetWasm deviation (speed): musl's pow ported to C# (MathPorts.Pow.cs), which the compiler can inline,
            // in place of the C library's call; it gives musl's results bit for bit on 50 million inputs; about 1.16
            // times as fast as the call in dotnet/performance's MathBenchmarks.
            return MathPorts.Pow(x, y);
        }

        [Intrinsic]
        public static double Sin(double a)
        {
            return RuntimeImports.sin(a);
        }

        [Intrinsic]
        public static double Sinh(double value)
        {
            return RuntimeImports.sinh(value);
        }

        [Intrinsic]
        public static double Sqrt(double d)
        {
            return RuntimeImports.sqrt(d);
        }

        [Intrinsic]
        public static double Tan(double a)
        {
            return RuntimeImports.tan(a);
        }

        [Intrinsic]
        public static double Tanh(double value)
        {
            return RuntimeImports.tanh(value);
        }

        [Intrinsic]
        private static unsafe double ModF(double x, double* intptr)
        {
            return RuntimeImports.modf(x, intptr);
        }

        // NetWasm deviation (speed): one call to the C library's sincos, which reduces the argument once for both,
        // in place of Sin and Cos; musl's sincos uses sin's and cos's kernels and gives their results bit for bit (on
        // 50 million inputs). CoreCLR also calls sincos.
        // Inlined, so the caller keeps both results as values instead of a returned pair in its own value frame.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe (double Sin, double Cos) SinCos(double x)
        {
            // The results land in a static at a fixed address: a local's address would put both in a value frame
            // that every caller allocates. sincos returns before anything else can use it.
            var results = (SinCosResults*)Unsafe.AsPointer(ref s_sinCosResults);
            RuntimeImports.sincos(x, &results->Sin, &results->Cos);
            return (results->Sin, results->Cos);
        }

        private static SinCosResults s_sinCosResults;

        private struct SinCosResults
        {
            public double Sin;
            public double Cos;
        }
    }
}
