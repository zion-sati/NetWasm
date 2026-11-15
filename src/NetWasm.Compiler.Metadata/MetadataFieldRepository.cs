// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Frozen;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataFieldRepository(
    FrozenDictionary<EntityKey, FieldDefinitionModel> fields,
    IMetadataAvailabilityValidator availability) : IFieldRepository
{
    public FieldDefinitionModel GetField(EntityKey key)
    {
        availability.Validate();
        return fields.TryGetValue(key, out var field)
            ? field
            : throw MissingEntity(key);
    }

    private static CompilerException MissingEntity(EntityKey key) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"field '{key}' does not exist"));
}
