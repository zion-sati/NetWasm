// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

// The attribute query services of one compilation: the specializer that resolves queries at compile time, and the
// reducer that answers queries on a Type known only at run time.
internal sealed class AttributeQueryServices(
    IMethodSpecializer specializer,
    NetWasm.Compiler.Analysis.Constants.IReachableProgramReducer runtimeTypeQueries,
    IUnplannedAttributeQueryFinder unplannedQueries)
{
    public IMethodSpecializer Specializer { get; } = specializer;
    public NetWasm.Compiler.Analysis.Constants.IReachableProgramReducer RuntimeTypeQueries { get; } = runtimeTypeQueries;
    // Finds the queries the settled program leaves to their run-time paths (NW1019).
    public IUnplannedAttributeQueryFinder UnplannedQueries { get; } = unplannedQueries;
}

internal interface IAttributeQueryMethodSpecializerFactory
{
    AttributeQueryServices Create(
        IMethodSpecializer specializer,
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fieldRepository,
        IMethodRepository methodRepository,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver typeIdentities,
        IMethodInstanceResolver methodInstances,
        ITypeRelationshipClassifier relationships,
        IBaseTypeResolver baseTypes,
        ICalledMethodResolver calls,
        ITypeOperandResolver types,
        ISymbolFormatter symbols);
}
