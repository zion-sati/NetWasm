using Microsoft.VisualStudio.TestPlatform.ObjectModel.Utilities;

namespace NetWasm.Testing.VSTest.Configuration;

internal sealed class NetWasmRunSettingsReader : INetWasmRunSettingsReader
{
    internal const string SupportedTargetFrameworkMoniker = "NetWasm,Version=v0.1";

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
            if (targetFramework is null ||
                !targetFramework.Equals(SupportedTargetFrameworkMoniker, StringComparison.Ordinal))
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
}
