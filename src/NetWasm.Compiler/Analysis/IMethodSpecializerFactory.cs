using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IMethodSpecializerFactory
{
    IMethodSpecializer Create(
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IFieldRepository fields,
        ITypeFinder types,
        ICalledMethodResolver calledMethods,
        IMethodImplementationResolver methodImplementations);
}
