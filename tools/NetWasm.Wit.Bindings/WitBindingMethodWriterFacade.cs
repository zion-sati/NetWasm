using System;

namespace NetWasm.Wit.Bindings;

public interface IWitBindingMethodWriter
{
    string Generate(WitDocument document, WitWorld world);
}

public sealed class WitBindingMethodWriter(
    IWitBindingImportSectionWriter imports,
    IWitBindingExportSectionWriter exports) : IWitBindingMethodWriter
{
    private readonly IWitBindingImportSectionWriter _imports = imports ??
        throw new ArgumentNullException(nameof(imports));
    private readonly IWitBindingExportSectionWriter _exports = exports ??
        throw new ArgumentNullException(nameof(exports));

    public string Generate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        return _imports.Generate(document, world) + _exports.Generate(document, world);
    }
}
