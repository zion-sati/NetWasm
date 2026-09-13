using System.Xml;
using System.Xml.Linq;

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
            var document = XDocument.Parse(runsettingsXml, LoadOptions.None);
            var runConfigurations = document.Descendants()
                .Where(element => element.Name.LocalName == "RunConfiguration")
                .Take(2)
                .ToArray();
            if (runConfigurations.Length != 1)
            {
                return false;
            }

            var targetFramework = SingleValue(runConfigurations[0], "TargetFrameworkVersion");
            if (!string.Equals(
                    targetFramework,
                    SupportedTargetFrameworkMoniker,
                    StringComparison.Ordinal))
            {
                return false;
            }

            configuration = new NetWasmRunConfiguration(
                targetFramework,
                OptionalSingleValue(runConfigurations[0], "DotnetHostPath"));
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string? OptionalSingleValue(XElement parent, string localName)
    {
        var values = parent.Elements()
            .Where(element => element.Name.LocalName == localName)
            .Take(2)
            .Select(element => element.Value)
            .ToArray();
        return values.Length switch
        {
            0 => null,
            1 when !string.IsNullOrWhiteSpace(values[0]) => values[0],
            _ => throw new XmlException($"Run settings contain an invalid {localName} value."),
        };
    }

    private static string SingleValue(XElement parent, string localName) =>
        OptionalSingleValue(parent, localName)
        ?? throw new XmlException($"Run settings do not contain {localName}.");
}
