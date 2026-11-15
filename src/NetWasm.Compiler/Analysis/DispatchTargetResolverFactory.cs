// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchTargetResolverFactory : IDispatchTargetResolverFactory
{
    public IDispatchTargetResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodImplementationResolver methodImplementations,
        IImplementedInterfaceResolver implementedInterfaces,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        ITypeRelationshipClassifier relationships,
        IBaseTypeResolver baseTypes) =>
        new DispatchTargetResolver(
            types,
            typeDefinitions,
            methods,
            methodImplementations,
            implementedInterfaces,
            methodInstances,
            symbols,
            relationships,
            baseTypes);
}
