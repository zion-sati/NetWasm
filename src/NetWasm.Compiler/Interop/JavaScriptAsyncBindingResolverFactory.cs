using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Interop;

internal interface IJavaScriptAsyncBindingResolverFactory
{
    IJavaScriptAsyncBindingResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IFieldRepository fields,
        IMethodRepository methodRepository,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols);
}

internal sealed class JavaScriptAsyncBindingResolverFactory :
    IJavaScriptAsyncBindingResolverFactory
{
    public IJavaScriptAsyncBindingResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IFieldRepository fields,
        IMethodRepository methodRepository,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methodRepository);
        ArgumentNullException.ThrowIfNull(methodInstances);
        ArgumentNullException.ThrowIfNull(symbols);
        return new JavaScriptAsyncBindingResolver(
            types,
            typeDefinitions,
            identities,
            fields,
            methodRepository,
            methodInstances,
            symbols);
    }
}
