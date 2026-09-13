using System;

namespace NetWasm.Compiler.ComponentModel.Worlds;

public sealed class WitWorldSpecifierFormatter : IWitWorldSpecifierFormatter
{
    public string Format(WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var versionIndex = world.Package.LastIndexOf('@');
        return versionIndex < 0
            ? $"{world.Package}/{world.Name}"
            : $"{world.Package[..versionIndex]}/{world.Name}{world.Package[versionIndex..]}";
    }
}
