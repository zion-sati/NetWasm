using System;

namespace NetWasm.Compiler.ComponentModel.Worlds;

public sealed class WitInterfaceSpecifierFormatter : IWitInterfaceSpecifierFormatter
{
    public string Format(WitInterface definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Key;
    }
}
