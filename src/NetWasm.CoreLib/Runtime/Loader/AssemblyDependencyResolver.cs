// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Reflection;
using System.Runtime.Versioning;

namespace System.Runtime.Loader
{
    // As upstream's build for platforms without a host's dependency manifest: a NetWasm program has no .deps.json and
    // loads no component at run time.
    [UnsupportedOSPlatform("android")]
    [UnsupportedOSPlatform("browser")]
    [UnsupportedOSPlatform("ios")]
    [UnsupportedOSPlatform("tvos")]
    public sealed class AssemblyDependencyResolver
    {
        public AssemblyDependencyResolver(string componentAssemblyPath)
        {
            throw new PlatformNotSupportedException();
        }

        public string? ResolveAssemblyToPath(AssemblyName assemblyName)
        {
            throw new PlatformNotSupportedException();
        }

        public string? ResolveUnmanagedDllToPath(string unmanagedDllName)
        {
            throw new PlatformNotSupportedException();
        }
    }
}
