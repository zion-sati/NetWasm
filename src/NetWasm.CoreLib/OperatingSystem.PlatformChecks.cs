// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de (OperatingSystem.cs's
// platform checks).
// NetWasm adaptations: one CoreLib serves every host, so the platform name, IsBrowser and IsWasi
// come from the compiled target (NetWasmHost, PlatformWorld) instead of TARGET_BROWSER and
// TARGET_WASI, and a plain core module, which has no WASI, is no OS platform at all; every other
// platform check keeps upstream's non-matching branch. Version checks compare against upstream's
// wasm OS version, 1.0.0.0, because Environment.OSVersion stays unavailable. NonVersionable is
// dropped; NetWasm has no versioning boundary for it to mark.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace System
{
    public sealed partial class OperatingSystem
    {
        private static string? OSPlatformName =>
            NetWasmHost.IsJavaScriptHost ? "BROWSER" : PlatformWorld.ProvidesWasi() ? "WASI" : null;

        /// <summary>
        /// Indicates whether the current application is running on the specified platform.
        /// </summary>
        /// <param name="platform">Case-insensitive platform name. Examples: Browser, Linux, FreeBSD, Android, iOS, macOS, tvOS, watchOS, Windows.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOSPlatform(string platform)
        {
            ArgumentNullException.ThrowIfNull(platform);

            return platform.Equals(OSPlatformName, StringComparison.OrdinalIgnoreCase)
#if TARGET_OSX
            || platform.Equals("MACOS", StringComparison.OrdinalIgnoreCase)
#elif TARGET_MACCATALYST
            || platform.Equals("IOS", StringComparison.OrdinalIgnoreCase)
#endif
            ;
        }

        /// <summary>
        /// Check for the OS with a >= version comparison. Used to guard APIs that were added in the given OS release.
        /// </summary>
        /// <param name="platform">Case-insensitive platform name. Examples: Browser, Linux, FreeBSD, Android, iOS, macOS, tvOS, watchOS, Windows.</param>
        /// <param name="major">Major OS version number.</param>
        /// <param name="minor">Minor OS version number (optional).</param>
        /// <param name="build">Build OS version number (optional).</param>
        /// <param name="revision">Revision OS version number (optional).</param>
        public static bool IsOSPlatformVersionAtLeast(string platform, int major, int minor = 0, int build = 0, int revision = 0)
            => IsOSPlatform(platform) && IsOSVersionAtLeast(major, minor, build, revision);

        /// <summary>
        /// Indicates whether the current application is running as WASM in a Browser.
        /// </summary>
        public static bool IsBrowser() => NetWasmHost.IsJavaScriptHost;

        /// <summary>
        /// Indicates whether the current application is running as WASI.
        /// </summary>
        public static bool IsWasi() => !NetWasmHost.IsJavaScriptHost && PlatformWorld.ProvidesWasi();

        /// <summary>
        /// Indicates whether the current application is running on Linux.
        /// </summary>
        public static bool IsLinux() =>
#if TARGET_LINUX && !TARGET_ANDROID
            true;
#else
            false;
#endif

        /// <summary>
        /// Indicates whether the current application is running on FreeBSD.
        /// </summary>
        public static bool IsFreeBSD() =>
#if TARGET_FREEBSD
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the FreeBSD version (returned by 'uname') with a >= version comparison. Used to guard APIs that were added in the given FreeBSD release.
        /// </summary>
        public static bool IsFreeBSDVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0)
            => IsFreeBSD() && IsOSVersionAtLeast(major, minor, build, revision);

        /// <summary>
        /// Indicates whether the current application is running on FreeBSD.
        /// </summary>
        internal static bool IsOpenBSD() =>
#if TARGET_OPENBSD
            true;
#else
            false;
#endif

        /// <summary>
        /// Indicates whether the current application is running on Haiku.
        /// </summary>
        internal static bool IsHaiku() =>
#if TARGET_HAIKU
            true;
#else
            false;
#endif

        /// <summary>
        /// Indicates whether the current application is running on Android.
        /// </summary>
        public static bool IsAndroid() =>
#if TARGET_ANDROID
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the Android API level (returned by 'ro.build.version.sdk') with a >= version comparison. Used to guard APIs that were added in the given Android release.
        /// </summary>
        public static bool IsAndroidVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0)
            => IsAndroid() && IsOSVersionAtLeast(major, minor, build, revision);

        /// <summary>
        /// Indicates whether the current application is running on iOS or MacCatalyst.
        /// </summary>
        [SupportedOSPlatformGuard("maccatalyst")]
        public static bool IsIOS() =>
#if TARGET_IOS || TARGET_MACCATALYST
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the iOS/MacCatalyst version (returned by 'libobjc.get_operatingSystemVersion') with a >= version comparison. Used to guard APIs that were added in the given iOS release.
        /// </summary>
        [SupportedOSPlatformGuard("maccatalyst")]
        public static bool IsIOSVersionAtLeast(int major, int minor = 0, int build = 0)
            => IsIOS() && IsOSVersionAtLeast(major, minor, build, 0);

        /// <summary>
        /// Indicates whether the current application is running on macOS.
        /// </summary>
        public static bool IsMacOS() =>
#if TARGET_OSX
            true;
#else
            false;
#endif

        internal static bool IsApplePlatform() =>
#if TARGET_OSX || TARGET_MACCATALYST || TARGET_IOS || TARGET_TVOS
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the macOS version (returned by 'libobjc.get_operatingSystemVersion') with a >= version comparison. Used to guard APIs that were added in the given macOS release.
        /// </summary>
        public static bool IsMacOSVersionAtLeast(int major, int minor = 0, int build = 0)
            => IsMacOS() && IsOSVersionAtLeast(major, minor, build, 0);

        /// <summary>
        /// Indicates whether the current application is running on Mac Catalyst.
        /// </summary>
        public static bool IsMacCatalyst() =>
#if TARGET_MACCATALYST
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the Mac Catalyst version (iOS version as presented in Apple documentation) with a >= version comparison. Used to guard APIs that were added in the given Mac Catalyst release.
        /// </summary>
        public static bool IsMacCatalystVersionAtLeast(int major, int minor = 0, int build = 0)
            => IsMacCatalyst() && IsOSVersionAtLeast(major, minor, build, 0);

        /// <summary>
        /// Indicates whether the current application is running on tvOS.
        /// </summary>
        public static bool IsTvOS() =>
#if TARGET_TVOS
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the tvOS version (returned by 'libobjc.get_operatingSystemVersion') with a >= version comparison. Used to guard APIs that were added in the given tvOS release.
        /// </summary>
        public static bool IsTvOSVersionAtLeast(int major, int minor = 0, int build = 0)
            => IsTvOS() && IsOSVersionAtLeast(major, minor, build, 0);

        /// <summary>
        /// Indicates whether the current application is running on watchOS.
        /// </summary>
        public static bool IsWatchOS() =>
#if TARGET_WATCHOS
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the watchOS version (returned by 'libobjc.get_operatingSystemVersion') with a >= version comparison. Used to guard APIs that were added in the given watchOS release.
        /// </summary>
        public static bool IsWatchOSVersionAtLeast(int major, int minor = 0, int build = 0)
            => IsWatchOS() && IsOSVersionAtLeast(major, minor, build, 0);

        /// <summary>
        /// Indicates whether the current application is running on Windows.
        /// </summary>
        public static bool IsWindows() =>
#if TARGET_WINDOWS
            true;
#else
            false;
#endif

        /// <summary>
        /// Check for the Windows version (returned by 'RtlGetVersion') with a >= version comparison. Used to guard APIs that were added in the given Windows release.
        /// </summary>
        public static bool IsWindowsVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0)
            => IsWindows() && IsOSVersionAtLeast(major, minor, build, revision);

        private static bool IsOSVersionAtLeast(int major, int minor, int build, int revision)
        {
            // Upstream's wasm targets report OS version 1.0.0.0; NetWasm compares against it directly
            // because Environment.OSVersion stays unavailable.
            Version current = new Version(1, 0, 0, 0);

            if (current.Major != major)
            {
                return current.Major > major;
            }
            if (current.Minor != minor)
            {
                return current.Minor > minor;
            }
            // Unspecified build component is to be treated as zero
            int currentBuild = current.Build < 0 ? 0 : current.Build;
            build = build < 0 ? 0 : build;
            if (currentBuild != build)
            {
                return currentBuild > build;
            }

            // Unspecified revision component is to be treated as zero
            int currentRevision = current.Revision < 0 ? 0 : current.Revision;
            revision = revision < 0 ? 0 : revision;

            return currentRevision >= revision;
        }
    }
}
