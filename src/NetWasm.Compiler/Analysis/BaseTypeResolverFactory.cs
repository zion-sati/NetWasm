// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class BaseTypeResolverFactory : IBaseTypeResolverFactory
{
    public IBaseTypeResolver Create(
        ITypeFinder types,
        ITypeIdentityResolver identities,
        IBaseTypeIdentityResolver baseTypes) =>
        new BaseTypeResolver(types, identities, baseTypes);
}
