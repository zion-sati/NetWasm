// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class FieldSignatureContextResolver : IFieldSignatureContextResolver
{
    public CliGenericContext Resolve(CliTypeIdentity declaringType) =>
        declaringType.Shape == CliTypeShape.GenericInstantiation
            ? new CliGenericContext(declaringType.TypeArguments, [])
            : CliGenericContext.Empty;
}
