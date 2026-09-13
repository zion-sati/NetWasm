using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

public interface IWholeProgramAnalyzerFactory
{
    IWholeProgramAnalyzer Create(
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fieldRepository,
        IMethodRepository methodRepository,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver typeIdentities,
        IBaseTypeIdentityResolver baseTypeIdentities,
        IImplementedInterfaceResolver implementedInterfaces,
        IMethodBodyReader methodBodies,
        IMethodInstanceResolver methodInstances,
        IMethodImplementationResolver methodImplementations,
        ISymbolFormatter symbols,
        Func<MethodDefinitionModel, JavaScriptAsyncMethodBinding?> javaScriptAsyncBindings,
        IRuntimeIntrinsicRegistry intrinsics);
}
