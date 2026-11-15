// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodImplementationResolver(
    IMetadataAssemblyResolver assemblies,
    IMetadataMethodReferenceResolver methodReferences,
    ITypeDefinitionResolver typeDefinitions) : IMethodImplementationResolver
{
    public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(
        CliTypeIdentity type)
    {
        var definition = ResolveTypeIdentity(type);
        var source = assemblies.Resolve(definition.Key.Assembly);
        var metadata = source.Reader.GetTypeDefinition(
            (TypeDefinitionHandle)MetadataTokens.Handle(definition.Key.MetadataToken));
        var context = new CliGenericContext(
            type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [],
            []);
        return [.. metadata.GetMethodImplementations().Select(handle =>
        {
            var implementation = source.Reader.GetMethodImplementation(handle);
            return new MethodImplementationInstanceModel(
                methodReferences.Resolve(
                    source,
                    MetadataTokens.GetToken(implementation.MethodBody),
                    definition.FullName,
                    0,
                    context),
                methodReferences.Resolve(
                    source,
                    MetadataTokens.GetToken(implementation.MethodDeclaration),
                    definition.FullName,
                    0,
                    context));
        })];
    }

    private TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
        typeDefinitions.ResolveTypeIdentity(identity);
}
