// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using Microsoft.VisualStudio.TestPlatform.ObjectModel.Utilities;

namespace NetWasm.Testing.VSTest.Configuration;

internal sealed class NetWasmRunSettingsReader : INetWasmRunSettingsReader
{
    internal const string SupportedTargetFrameworkMoniker = "NetWasm,Version=v0.1";

    // VSTest receives a platform target framework such as net10.0-netwasm as its .NETCoreApp framework, the same
    // as a desktop project; NetWasm.Testing.VSTest's build adds this test run parameter with the project's alias.
    internal const string PlatformTargetFrameworkParameter = "NetWasm.TargetFramework";
    private const string PlatformFrameworkPrefix = ".NETCoreApp,";
    private const string PlatformAliasMarker = "-netwasm";

    public bool TryRead(string? runsettingsXml, out NetWasmRunConfiguration? configuration)
    {
        configuration = null;
        if (string.IsNullOrWhiteSpace(runsettingsXml))
        {
            return false;
        }

        try
        {
            var runConfiguration = XmlRunSettingsUtilities.GetRunConfigurationNode(runsettingsXml);
            if (!runConfiguration.TargetFrameworkSet)
            {
                return false;
            }

            var targetFramework = runConfiguration.TargetFramework?.Name;
            var parameters = XmlRunSettingsUtilities.GetTestRunParameters(runsettingsXml);
            var platformAlias = parameters.TryGetValue(PlatformTargetFrameworkParameter, out var value)
                ? value as string ?? string.Empty
                : null;
            if (targetFramework is null || !IsNetWasmRun(targetFramework, platformAlias))
            {
                return false;
            }

            configuration = new NetWasmRunConfiguration(
                targetFramework,
                string.IsNullOrWhiteSpace(runConfiguration.DotnetHostPath)
                    ? null
                    : runConfiguration.DotnetHostPath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // netwasm0.1 has its own framework name. A platform alias makes a .NETCoreApp run NetWasm's, and only that.
    private static bool IsNetWasmRun(string targetFramework, string? platformAlias) =>
        platformAlias is null
            ? targetFramework.Equals(SupportedTargetFrameworkMoniker, StringComparison.Ordinal)
            : platformAlias.Contains(PlatformAliasMarker, StringComparison.OrdinalIgnoreCase)
                && targetFramework.StartsWith(PlatformFrameworkPrefix, StringComparison.Ordinal);
}
