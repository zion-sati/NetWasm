// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal enum AttributeQueryApiKind
{
    Instance,
    AttributeStatic,
    Extension,
}

internal sealed record AttributeQueryCall(
    AttributeQueryOperation Operation,
    int ArgumentCount,
    int? FilterArgument,
    CliTypeIdentity? GenericFilter,
    int? InheritArgument);
