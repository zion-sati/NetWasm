// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record ConstrainedCallTargetRequest(
    CliTypeIdentity ConstrainedType,
    TypeDefinitionModel ConcreteType,
    MethodDefinitionModel Declaration,
    MethodSignatureModel CallSignature,
    ImmutableArray<CliTypeIdentity> TypeArguments,
    ImmutableArray<CliTypeIdentity> MethodArguments);
