// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Frozen;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodRepository(
    FrozenDictionary<EntityKey, MethodDefinitionModel> methods,
    IMetadataAvailabilityValidator availability) : IMethodRepository
{
    public MethodDefinitionModel GetMethod(EntityKey key)
    {
        availability.Validate();
        return methods.TryGetValue(key, out var method)
            ? method
            : throw MissingEntity(key);
    }

    private static CompilerException MissingEntity(EntityKey key) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"method '{key}' does not exist"));
}
