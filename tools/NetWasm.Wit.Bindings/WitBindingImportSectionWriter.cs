using System;
using NetWasm.Wit.Bindings.FlatSlots;

namespace NetWasm.Wit.Bindings;

public interface IWitBindingImportSectionWriter
{
    string Generate(WitDocument document, WitWorld world);
}

public sealed class WitBindingImportSectionWriter(
    IWitCanonicalTypeResolver types,
    NetWasm.Compiler.Core.ICanonicalAbiTypeFlattener flattener,
    NetWasm.Compiler.Core.ICanonicalAbiMemoryLayoutPlanner layouts,
    IWitBindingFunctionModelBuilder models,
    IWitBindingSyntaxFormatter syntax,
    IWitFlatSlotCoercionFormatter slotCoercions,
    ICodeWriterFactory writers) :
    WitBindingMethodSectionBase(
        types, flattener, layouts, models, syntax, slotCoercions),
    IWitBindingImportSectionWriter
{
    private readonly ICodeWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));

    public string Generate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        var writer = _writers.Create();
        WriteImports(writer, document, world);
        return writer.Text;
    }
}
