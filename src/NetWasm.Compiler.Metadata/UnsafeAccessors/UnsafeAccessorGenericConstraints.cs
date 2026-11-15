// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed record UnsafeAccessorGenericConstraints(
    ImmutableArray<UnsafeAccessorGenericParameterConstraints> TypeParameters,
    ImmutableArray<UnsafeAccessorGenericParameterConstraints> MethodParameters);

internal sealed record UnsafeAccessorGenericParameterConstraints(
    GenericParameterAttributes Attributes,
    ImmutableArray<CliTypeIdentity> Types);
