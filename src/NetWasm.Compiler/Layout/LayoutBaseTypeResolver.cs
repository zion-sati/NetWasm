// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class LayoutBaseTypeResolver(
    ITypeFinder types,
    ITypeIdentityResolver identities,
    IMetadataIdentityBaseTypeResolver baseTypes) : IBaseTypeResolver
{
    public CliTypeIdentity? Resolve(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.SzArray or CliTypeShape.Array =>
            identities.GetTypeIdentity(types.FindType("System.Array").Key),
        // A pointer, a function pointer and a by-reference type have no base type, as .NET reports them.
        CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer or
            CliTypeShape.GenericTypeParameter or
            CliTypeShape.GenericMethodParameter => null,
        _ => baseTypes.GetBaseType(type),
    };
}
