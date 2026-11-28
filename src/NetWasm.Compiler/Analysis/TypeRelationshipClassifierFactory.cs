// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeRelationshipClassifierFactory :
    ITypeRelationshipClassifierFactory
{
    public ITypeRelationshipClassifier Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IImplementedInterfaceResolver interfaces,
        IBaseTypeResolver baseTypes) =>
        new TypeRelationshipClassifier(
            types,
            typeDefinitions,
            identities,
            interfaces,
            baseTypes);
}

// The classifier for a caller that classifies each pair once (assignability metadata): it remembers only the pairs it
// classifies on the way.
internal sealed class UnrememberedTypeRelationshipClassifierFactory : IUnrememberedTypeRelationshipClassifierFactory
{
    public ITypeRelationshipClassifier Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IImplementedInterfaceResolver interfaces,
        IBaseTypeResolver baseTypes) =>
        new TypeRelationshipClassifier(
            types,
            typeDefinitions,
            identities,
            interfaces,
            baseTypes,
            remembersClassifiedPairs: false);
}
