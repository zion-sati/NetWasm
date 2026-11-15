// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IDispatchTargetResolverFactory
{
    IDispatchTargetResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodImplementationResolver methodImplementations,
        IImplementedInterfaceResolver implementedInterfaces,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        ITypeRelationshipClassifier relationships,
        IBaseTypeResolver baseTypes);
}
