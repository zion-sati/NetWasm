using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawWitBindingPlan(RawImportBindingSelection Selection, ImmutableArray<RawWitImportLayout> Imports);

public interface IRawWitBindingPlanBuilder
{
    RawWitBindingPlan Build(RawImportBindingSelectionRequest request);
}

public sealed class RawWitBindingPlanBuilder(
    IRawImportBindingSelector selections,
    IRawWitImportLayoutBuilder layouts) : IRawWitBindingPlanBuilder
{
    private readonly IRawImportBindingSelector _selections = selections ?? throw new ArgumentNullException(nameof(selections));
    private readonly IRawWitImportLayoutBuilder _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));

    public RawWitBindingPlan Build(RawImportBindingSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selection = _selections.SelectBindings(request);
        var imports = ImmutableArray.CreateBuilder<RawWitImportLayout>();
        foreach (var identity in selection.WitImports)
        {
            var declaration = selection.Catalog.Imports[identity];
            var layout = _layouts.Build(new(selection.Catalog.Document, declaration, selection.Catalog.Target));
            if (layout.Identity != identity || layout.Target != selection.Catalog.Target)
            {
                throw ComponentException.Invalid("raw WIT layout does not match the selected import identity and target");
            }
            imports.Add(layout);
        }
        return new(selection, imports.ToImmutable());
    }
}
