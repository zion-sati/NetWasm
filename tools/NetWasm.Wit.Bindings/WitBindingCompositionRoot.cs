using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Wit.Bindings.Aliases;
using NetWasm.Wit.Bindings.FlatSlots;
using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings;

public static class WitBindingCompositionRoot
{
    public static IWitCSharpBindingGenerator Create()
    {
        var definitions = new WitTypeDefinitionClassifier();
        var syntax = new WitBindingSyntaxFormatter(definitions);
        var writers = new CodeWriterFactory();
        var canonicalTypes = new WitCanonicalTypeResolver();
        var flattener = new CanonicalAbiTypeFlattener();
        var signatures = new CanonicalAbiSignaturePlanner(flattener);
        var layouts = new CanonicalAbiMemoryLayoutPlanner();
        var models = new WitBindingFunctionModelBuilder(new WitCanonicalFunctionBuilder(canonicalTypes), signatures);
        var slotCoercions = new WitFlatSlotCoercionFormatter();

        var imports = new WitBindingImportSectionWriter(
            canonicalTypes,
            flattener,
            layouts,
            models,
            syntax,
            slotCoercions,
            writers);
        var exports = new WitBindingExportSectionWriter(
            canonicalTypes,
            flattener,
            layouts,
            models,
            syntax,
            slotCoercions,
            writers);
        var flat = new WitBindingFlatSectionWriter(
            canonicalTypes,
            flattener,
            layouts,
            models,
            syntax,
            slotCoercions,
            writers);
        var methods = new WitBindingMethodWriter(imports, exports, flat);
        var types = new WitTypeDeclarationWriter(
            syntax,
            writers,
            new WitAliasValidator(syntax),
            definitions);
        var marshalling = new WitCanonicalMarshallingWriter(
            new WitCanonicalMarshallingTypeSectionWriter(
                canonicalTypes,
                layouts,
                new WitCanonicalTypeReachabilityResolver(),
                syntax,
                writers));

        return new WitCSharpBindingGenerator(
            new WitWorldValidator(),
            types,
            methods,
            marshalling,
            syntax,
            writers);
    }
}
