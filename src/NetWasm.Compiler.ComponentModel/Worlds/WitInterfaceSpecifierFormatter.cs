using System;

namespace NetWasm.Compiler.ComponentModel.Worlds;

public sealed class WitInterfaceSpecifierFormatter : IWitInterfaceSpecifierFormatter
{
    public string Format(WitInterface definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var versionIndex = definition.Package.LastIndexOf('@');
        return versionIndex < 0
            ? $"{definition.Package}/{definition.Name}"
            : $"{definition.Package[..versionIndex]}/{definition.Name}{definition.Package[versionIndex..]}";
    }
}
