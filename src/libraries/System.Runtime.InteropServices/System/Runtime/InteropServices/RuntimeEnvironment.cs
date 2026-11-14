// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: GetRuntimeDirectory skips upstream's read of typeof(object).Assembly.Location. A NetWasm
// application is one compiled module, which like a single-file application has no assembly location, so upstream's
// fallback always applies: the application's base directory.

using System.IO;
using System.Reflection;

namespace System.Runtime.InteropServices
{
    public static class RuntimeEnvironment
    {
        [Obsolete(Obsoletions.RuntimeEnvironmentMessage, DiagnosticId = Obsoletions.RuntimeEnvironmentDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public static string SystemConfigurationFile => throw new PlatformNotSupportedException();

        public static bool FromGlobalAccessCache(Assembly a) => false;

        public static string GetRuntimeDirectory()
        {
            string runtimeDirectory = AppDomain.CurrentDomain.BaseDirectory;

            char sep = Path.DirectorySeparatorChar;
            return string.Concat(Path.GetDirectoryName(runtimeDirectory), new ReadOnlySpan<char>(in sep));
        }

        [Obsolete(Obsoletions.RuntimeEnvironmentMessage, DiagnosticId = Obsoletions.RuntimeEnvironmentDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public static IntPtr GetRuntimeInterfaceAsIntPtr(Guid clsid, Guid riid) => throw new PlatformNotSupportedException();

        [Obsolete(Obsoletions.RuntimeEnvironmentMessage, DiagnosticId = Obsoletions.RuntimeEnvironmentDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public static object GetRuntimeInterfaceAsObject(Guid clsid, Guid riid) => throw new PlatformNotSupportedException();

        public static string GetSystemVersion() => $"v{Environment.Version}";
    }
}
