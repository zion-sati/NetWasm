// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IExplicitValueLayoutResolver
{
    ExplicitValueLayoutPlan Resolve(
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        ImmutableArray<ExplicitFieldStorage> instanceFields,
        WasmTargetLayout target);
}

internal readonly record struct ExplicitFieldStorage(
    FieldDefinitionModel Definition,
    ValueLayout Layout);

internal sealed record ExplicitValueLayoutPlan(
    ValueLayout Value,
    ImmutableArray<FieldLayout> Fields);
