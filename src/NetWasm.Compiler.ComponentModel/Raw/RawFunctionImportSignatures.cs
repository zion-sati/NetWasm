// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public enum RawCoreValueType
{
    I32,
    I64,
    F32,
    F64,
}

public sealed record RawCliFunctionImportSignature(
    RawCanonicalImportIdentity Identity,
    ImmutableArray<CliValueKind> Parameters,
    CliValueKind Result);

public sealed record RawCoreFunctionImportSignature(
    RawCanonicalImportIdentity Identity,
    ImmutableArray<RawCoreValueType> Parameters,
    ImmutableArray<RawCoreValueType> Results);
