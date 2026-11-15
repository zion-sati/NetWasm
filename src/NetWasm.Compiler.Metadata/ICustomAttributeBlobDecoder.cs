// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICustomAttributeBlobDecoder
{
    CustomAttributeValue<CliTypeIdentity> Decode(BlobReader value, ImmutableArray<CliTypeIdentity> parameters);
}
