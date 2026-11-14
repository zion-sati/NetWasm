// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (RuntimeInformation.cs
// and RuntimeInformation.Browser.cs).
// NetWasm adaptations: one CoreLib serves every host, so the answers that upstream selects with
// TARGET_BROWSER and TARGET_WASI come from the compiled target (NetWasmHost): JavaScript-hosted
// builds report Browser and browser-wasm, WIT components report WASI and wasi-wasm. Upstream reads
// the runtime identifier from AppContext data set by its host; NetWasm has no such host property.
// FrameworkDescription, which upstream generates as ".NET " plus the product version, reports
// NetWasm's own version.

namespace System.Runtime.InteropServices
{
    public static partial class RuntimeInformation
    {
        /// <summary>
        /// Returns an opaque string that identifies the platform on which an app is running.
        /// </summary>
        public static string RuntimeIdentifier => NetWasmHost.IsJavaScriptHost ? "browser-wasm" : "wasi-wasm";

        /// <summary>Gets the name of the framework the application runs on.</summary>
        public static string FrameworkDescription => "NetWasm " + NetWasmProductVersion.Value;

        /// <summary>
        /// Indicates whether the current application is running on the specified platform.
        /// </summary>
        public static bool IsOSPlatform(OSPlatform osPlatform) => OperatingSystem.IsOSPlatform(osPlatform.Name);

        /// <summary>Gets the platform architecture of the currently running app.</summary>
        public static Architecture ProcessArchitecture => Architecture.Wasm;

        /// <summary>Gets a string that describes the operating system on which the app is running.</summary>
        public static string OSDescription => NetWasmHost.IsJavaScriptHost ? "Browser" : "WASI";

        /// <summary>Gets the platform architecture on which the current app is running.</summary>
        public static Architecture OSArchitecture => Architecture.Wasm;
    }
}
