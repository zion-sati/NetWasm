// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Frozen;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeRepository(
    FrozenDictionary<EntityKey, TypeDefinitionModel> types,
    IMetadataAvailabilityValidator availability) : ITypeRepository
{
    public TypeDefinitionModel GetTypeDefinition(EntityKey key)
    {
        availability.Validate();
        return types.TryGetValue(key, out var type)
            ? type
            : throw MissingEntity(key);
    }

    private static CompilerException MissingEntity(EntityKey key) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"type '{key}' does not exist"));
}
