// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Any value type of the compilation, as a struct's native layout reads it (NativeStructLayouts).
internal sealed class CompilationNativeStructSource(ITypeDefinitionResolver definitions, IFieldRepository fields)
    : INativeStructSource
{
    public (TypeDefinitionModel Definition, ImmutableArray<FieldDefinitionModel> Fields)? Describe(CliTypeIdentity type)
    {
        var definition = definitions.ResolveTypeIdentity(type);
        return (definition, [.. definition.Fields.Select(fields.GetField).Where(field => !field.IsStatic)]);
    }
}
