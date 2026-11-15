// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IAssignableTypeMetadataBuilder
{
    // The runtime type IDs a cast to the candidate accepts, ascending.
    ImmutableArray<int> Build(CliTypeIdentity candidate);
}
