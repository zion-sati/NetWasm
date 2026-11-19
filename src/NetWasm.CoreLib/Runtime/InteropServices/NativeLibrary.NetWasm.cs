// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Runtime.InteropServices
{
    // Resolves a native library for an assembly's P/Invokes, as .NET's DllImportResolver does.
    public delegate IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath);

    // .NET's NativeLibrary, where native code is linked into the program when it is built: no library can be loaded
    // while it runs, so every load answers as .NET does for a library that is not there (DllNotFoundException, or false
    // from the Try methods), and an export lookup as for a symbol that is not there. A P/Invoke binds to the linked
    // archive at build time, so a registered DllImportResolver is recorded, with .NET's checks, and never consulted.
    public static class NativeLibrary
    {
        private static ConditionalWeakTable<Assembly, DllImportResolver>? s_resolvers;

        public static IntPtr Load(string libraryPath)
        {
            ArgumentNullException.ThrowIfNull(libraryPath);
            throw NotFound(libraryPath);
        }

        public static bool TryLoad(string libraryPath, out IntPtr handle)
        {
            ArgumentNullException.ThrowIfNull(libraryPath);
            handle = IntPtr.Zero;
            return false;
        }

        public static IntPtr Load(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            ArgumentNullException.ThrowIfNull(libraryName);
            ArgumentNullException.ThrowIfNull(assembly);
            throw NotFound(libraryName);
        }

        public static bool TryLoad(string libraryName, Assembly assembly, DllImportSearchPath? searchPath, out IntPtr handle)
        {
            ArgumentNullException.ThrowIfNull(libraryName);
            ArgumentNullException.ThrowIfNull(assembly);
            handle = IntPtr.Zero;
            return false;
        }

        // No handle can come from Load, so there is nothing to free.
        public static void Free(IntPtr handle)
        {
        }

        public static IntPtr GetExport(IntPtr handle, string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            throw new EntryPointNotFoundException(SR.Format(SR.Arg_EntryPointNotFoundExceptionParameterizedNoLibrary, name));
        }

        public static bool TryGetExport(IntPtr handle, string name, out IntPtr address)
        {
            ArgumentNullException.ThrowIfNull(name);
            address = IntPtr.Zero;
            return false;
        }

        public static void SetDllImportResolver(Assembly assembly, DllImportResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(resolver);
            if (s_resolvers is null)
            {
                Interlocked.CompareExchange(ref s_resolvers, new ConditionalWeakTable<Assembly, DllImportResolver>(), null);
            }
            if (!s_resolvers.TryAdd(assembly, resolver))
            {
                throw new InvalidOperationException(SR.InvalidOperation_CannotRegisterSecondResolver);
            }
        }

        // The program has no module handle to resolve exports through.
        public static IntPtr GetMainProgramHandle() => throw new PlatformNotSupportedException();

        private static DllNotFoundException NotFound(string libraryName) =>
            new(SR.Format(SR.DllNotFound_NetWasm, libraryName));
    }
}
