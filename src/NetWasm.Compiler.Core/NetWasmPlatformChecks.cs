// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core;

/// <summary>
/// The platform checks that are false on every NetWasm host. NetWasm.CoreLib keeps upstream's non-matching branch for
/// every operating system but the browser and WASI, so OperatingSystem's checks for the others, and their version
/// forms, are false whatever their arguments; so is a platform test of any other literal name or of OSPlatform's
/// Windows, Linux, OSX and FreeBSD properties.
/// </summary>
public static class NetWasmPlatformChecks
{
    public const string OperatingSystem = "System.OperatingSystem";
    public const string RuntimeInformation = "System.Runtime.InteropServices.RuntimeInformation";
    public const string OSPlatform = "System.Runtime.InteropServices.OSPlatform";
    public const string RuntimeFeature = "System.Runtime.CompilerServices.RuntimeFeature";

    /// <summary>A RuntimeFeature property getter that is false on NetWasm: no code is generated or compiled at run time,
    /// and there is one thread.</summary>
    public static bool IsNeverTrueRuntimeFeature(string getter) => getter is
        "get_IsDynamicCodeSupported" or "get_IsDynamicCodeCompiled" or "get_IsMultithreadingSupported";

    /// <summary>A static call that takes no arguments and is false on NetWasm, as a feature switch or an operating system
    /// test is read.</summary>
    public static bool IsNeverTrueParameterlessCall(string declaringType, string name) => declaringType switch
    {
        RuntimeFeature => IsNeverTrueRuntimeFeature(name),
        OperatingSystem => IsNeverTrueOperatingSystemCheck(name),
        _ => false,
    };

    /// <summary>An OperatingSystem method that is false on NetWasm whatever its arguments.</summary>
    public static bool IsNeverTrueOperatingSystemCheck(string name) => name is
        "IsWindows" or "IsWindowsVersionAtLeast" or "IsLinux" or "IsFreeBSD" or "IsFreeBSDVersionAtLeast" or
        "IsAndroid" or "IsAndroidVersionAtLeast" or "IsIOS" or "IsIOSVersionAtLeast" or "IsMacOS" or
        "IsMacOSVersionAtLeast" or "IsMacCatalyst" or "IsMacCatalystVersionAtLeast" or "IsTvOS" or
        "IsTvOSVersionAtLeast" or "IsWatchOS" or "IsWatchOSVersionAtLeast";

    /// <summary>A platform name OperatingSystem.IsOSPlatform never matches on NetWasm.</summary>
    public static bool IsNeverMatchingPlatformName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return !name.Equals("WASI", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("BROWSER", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An OSPlatform property RuntimeInformation.IsOSPlatform never matches on NetWasm.</summary>
    public static bool IsNeverMatchingOSPlatformGetter(string name) =>
        name is "get_Windows" or "get_Linux" or "get_OSX" or "get_FreeBSD";
}
