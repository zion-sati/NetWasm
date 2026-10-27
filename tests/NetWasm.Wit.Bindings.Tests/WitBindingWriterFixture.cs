using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Wit.Bindings.FlatSlots;
using NetWasm.Compiler.Core;

using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

internal static class WitBindingWriterFixture
{
    public static IWitBindingImportSectionWriter CreateImports() =>
        CreateSections().Imports;

    public static IWitBindingExportSectionWriter CreateExports() =>
        CreateSections().Exports;

    public static IWitBindingMethodWriter Create()
    {
        var sections = CreateSections();
        return new WitBindingMethodWriter(sections.Imports, sections.Exports);
    }

    private static (
        IWitBindingImportSectionWriter Imports,
        IWitBindingExportSectionWriter Exports) CreateSections()
    {
        var syntax = new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier());
        var types = new WitCanonicalTypeResolver();
        var flattener = new CanonicalAbiTypeFlattener();
        var signatures = new CanonicalAbiSignaturePlanner(flattener);
        var layouts = new CanonicalAbiMemoryLayoutPlanner();
        var models = new WitBindingFunctionModelBuilder(new WitCanonicalFunctionBuilder(types), signatures);
        var slotCoercions = new WitFlatSlotCoercionFormatter();
        return (
            new WitBindingImportSectionWriter(
                types,
                flattener,
                layouts,
                models,
                syntax,
                slotCoercions,
                new CodeWriterFactory()),
            new WitBindingExportSectionWriter(
                types,
                flattener,
                layouts,
                models,
                syntax,
                slotCoercions,
                new CodeWriterFactory()));
    }
}
