using System;
using System.Collections.Generic;
using NetWasm.Wit.Bindings.FlatSlots;

namespace NetWasm.Wit.Bindings;

public interface IWitBindingFlatSectionWriter
{
    WitBindingSectionSource Generate(
        WitDocument document,
        IReadOnlyCollection<int> lift,
        IReadOnlyCollection<int> lower);
}

public sealed class WitBindingFlatSectionWriter(
    IWitCanonicalTypeResolver types,
    NetWasm.Compiler.Core.ICanonicalAbiTypeFlattener flattener,
    NetWasm.Compiler.Core.ICanonicalAbiMemoryLayoutPlanner layouts,
    IWitBindingFunctionModelBuilder models,
    IWitBindingSyntaxFormatter syntax,
    IWitFlatSlotCoercionFormatter slotCoercions,
    ICodeWriterFactory writers) :
    WitBindingMethodSectionBase(
        types, flattener, layouts, models, syntax, slotCoercions),
    IWitBindingFlatSectionWriter
{
    private readonly ICodeWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));

    public WitBindingSectionSource Generate(
        WitDocument document,
        IReadOnlyCollection<int> lift,
        IReadOnlyCollection<int> lower)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lift);
        ArgumentNullException.ThrowIfNull(lower);
        var typeSet = new FlatBindingTypeSet([.. lift], [.. lower]);
        var writer = _writers.Create();
        WriteFlatBindingMethods(writer, document, typeSet);
        return new(writer.Text, [], []);
    }
}
