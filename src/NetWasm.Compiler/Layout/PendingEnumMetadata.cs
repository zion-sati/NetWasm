// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed record PendingEnumMetadata(
    EntityKey Type,
    CliTypeIdentity EnumType,
    int TypeId,
    CliTypeIdentity UnderlyingType,
    bool IsFlags,
    bool IsOpenDefinition,
    ImmutableArray<EnumMemberModel> Members,
    EnumMetadataPayload Payload);
