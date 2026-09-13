using System;
using System.Linq;

namespace NetWasm.Wit.Bindings;

public interface IWitBindingMethodWriter
{
    string Generate(WitDocument document, WitWorld world);
}

public sealed class WitBindingMethodWriter(
    IWitBindingImportSectionWriter imports,
    IWitBindingExportSectionWriter exports,
    IWitBindingFlatSectionWriter flat) : IWitBindingMethodWriter
{
    private readonly IWitBindingImportSectionWriter _imports = imports ??
        throw new ArgumentNullException(nameof(imports));
    private readonly IWitBindingExportSectionWriter _exports = exports ??
        throw new ArgumentNullException(nameof(exports));
    private readonly IWitBindingFlatSectionWriter _flat = flat ??
        throw new ArgumentNullException(nameof(flat));

    public string Generate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        var imports = _imports.Generate(document, world);
        var exports = _exports.Generate(document, world);
        var flat = _flat.Generate(
            document,
            imports.LiftTypes.Concat(exports.LiftTypes).Distinct().ToArray(),
            imports.LowerTypes.Concat(exports.LowerTypes).Distinct().ToArray());
        return imports.Source + exports.Source + flat.Source;
    }
}
