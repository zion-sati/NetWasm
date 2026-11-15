// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;

namespace NetWasm.Compiler.Core.GenericConstraints;

public sealed record GenericParameterConstraintModel(
    GenericParameterAttributes Attributes,
    ImmutableArray<CliTypeIdentity> Types);
