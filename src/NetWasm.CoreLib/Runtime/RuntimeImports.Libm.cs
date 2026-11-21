// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// src/coreclr/nativeaot/System.Private.CoreLib/src/System/Runtime/RuntimeImports.cs (its libm declarations).
// NetWasm adaptation: [LibraryImport] against libc in place of [RuntimeImport(RuntimeLibrary, ...)].

// The C library's math functions, which Math and MathF call as upstream's runtimes do: CoreCLR and Mono through
// internal calls into the C runtime, Native AOT through these RuntimeImports (Native AOT's RuntimeImports.cs, from
// which these declarations are taken in its order). NetWasm declares them with LibraryImport against libc, which
// every program links: the runtime pack's libc.a, Emscripten's musl, the libm Mono's WebAssembly runtime calls.

using System.Runtime.InteropServices;

namespace System.Runtime
{
    internal static partial class RuntimeImports
    {
        [LibraryImport(Interop.Libraries.libc, EntryPoint = "acos")]
        internal static partial double acos(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "acosf")]
        internal static partial float acosf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "acosh")]
        internal static partial double acosh(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "acoshf")]
        internal static partial float acoshf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "asin")]
        internal static partial double asin(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "asinf")]
        internal static partial float asinf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "asinh")]
        internal static partial double asinh(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "asinhf")]
        internal static partial float asinhf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "atan")]
        internal static partial double atan(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "atanf")]
        internal static partial float atanf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "atan2")]
        internal static partial double atan2(double y, double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "atan2f")]
        internal static partial float atan2f(float y, float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "atanh")]
        internal static partial double atanh(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "atanhf")]
        internal static partial float atanhf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "cbrt")]
        internal static partial double cbrt(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "cbrtf")]
        internal static partial float cbrtf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "ceil")]
        internal static partial double ceil(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "ceilf")]
        internal static partial float ceilf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "cos")]
        internal static partial double cos(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "cosf")]
        internal static partial float cosf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "cosh")]
        internal static partial double cosh(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "coshf")]
        internal static partial float coshf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "exp")]
        internal static partial double exp(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "expf")]
        internal static partial float expf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "floor")]
        internal static partial double floor(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "floorf")]
        internal static partial float floorf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "log")]
        internal static partial double log(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "logf")]
        internal static partial float logf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "log2")]
        internal static partial double log2(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "log2f")]
        internal static partial float log2f(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "log10")]
        internal static partial double log10(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "log10f")]
        internal static partial float log10f(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "pow")]
        internal static partial double pow(double x, double y);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "powf")]
        internal static partial float powf(float x, float y);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sin")]
        internal static partial double sin(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sinf")]
        internal static partial float sinf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sinh")]
        internal static partial double sinh(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sinhf")]
        internal static partial float sinhf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sqrt")]
        internal static partial double sqrt(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sqrtf")]
        internal static partial float sqrtf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "tan")]
        internal static partial double tan(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "tanf")]
        internal static partial float tanf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "tanh")]
        internal static partial double tanh(double x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "tanhf")]
        internal static partial float tanhf(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "fmod")]
        internal static partial double fmod(double x, double y);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "fmodf")]
        internal static partial float fmodf(float x, float y);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "fma")]
        internal static partial double fma(double x, double y, double z);

        // NetWasm: functions upstream's RuntimeImports do not declare, which Math and MathF call in place of a
        // composition of the ones it does (see their comments).
        [LibraryImport(Interop.Libraries.libc, EntryPoint = "exp2f")]
        internal static partial float exp2f(float x);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sincos")]
        internal static unsafe partial void sincos(double x, double* sin, double* cos);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "sincosf")]
        internal static unsafe partial void sincosf(float x, float* sin, float* cos);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "fmaf")]
        internal static partial float fmaf(float x, float y, float z);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "modf")]
        internal static unsafe partial double modf(double x, double* intptr);

        [LibraryImport(Interop.Libraries.libc, EntryPoint = "modff")]
        internal static unsafe partial float modff(float x, float* intptr);
    }
}
