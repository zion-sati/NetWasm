// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

// The platform checks false on every NetWasm host: every operating system but the browser and WASI, with their
// version forms; any platform name but WASI's and the browser's, case-insensitively; OSPlatform's four properties.
public sealed class NetWasmPlatformChecksTests
{
    [Theory]
    [InlineData("IsWindows", true)]
    [InlineData("IsWindowsVersionAtLeast", true)]
    [InlineData("IsLinux", true)]
    [InlineData("IsFreeBSD", true)]
    [InlineData("IsFreeBSDVersionAtLeast", true)]
    [InlineData("IsAndroid", true)]
    [InlineData("IsAndroidVersionAtLeast", true)]
    [InlineData("IsIOS", true)]
    [InlineData("IsIOSVersionAtLeast", true)]
    [InlineData("IsMacOS", true)]
    [InlineData("IsMacOSVersionAtLeast", true)]
    [InlineData("IsMacCatalyst", true)]
    [InlineData("IsMacCatalystVersionAtLeast", true)]
    [InlineData("IsTvOS", true)]
    [InlineData("IsTvOSVersionAtLeast", true)]
    [InlineData("IsWatchOS", true)]
    [InlineData("IsWatchOSVersionAtLeast", true)]
    [InlineData("IsBrowser", false)]
    [InlineData("IsWasi", false)]
    [InlineData("IsOSPlatform", false)]
    [InlineData("IsOSPlatformVersionAtLeast", false)]
    public void OtherOperatingSystemsNeverMatch(string check, bool never) =>
        Assert.Equal(never, NetWasmPlatformChecks.IsNeverTrueOperatingSystemCheck(check));

    [Theory]
    [InlineData("windows", true)]
    [InlineData("LINUX", true)]
    [InlineData("", true)]
    [InlineData("WASI", false)]
    [InlineData("wasi", false)]
    [InlineData("Browser", false)]
    public void PlatformNamesOtherThanWasiAndTheBrowserNeverMatch(string name, bool never) =>
        Assert.Equal(never, NetWasmPlatformChecks.IsNeverMatchingPlatformName(name));

    [Fact]
    public void APlatformNameIsRequired() =>
        Assert.Throws<ArgumentNullException>(() => NetWasmPlatformChecks.IsNeverMatchingPlatformName(null!));

    [Theory]
    [InlineData("get_Windows", true)]
    [InlineData("get_Linux", true)]
    [InlineData("get_OSX", true)]
    [InlineData("get_FreeBSD", true)]
    [InlineData("Create", false)]
    [InlineData("get_Name", false)]
    public void OSPlatformsFourPropertiesNeverMatch(string getter, bool never) =>
        Assert.Equal(never, NetWasmPlatformChecks.IsNeverMatchingOSPlatformGetter(getter));
}
