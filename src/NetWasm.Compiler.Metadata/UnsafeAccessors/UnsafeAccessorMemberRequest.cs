// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed record UnsafeAccessorMemberRequest(
    UnsafeAccessorMemberKind Kind,
    string Name,
    MethodDefinitionModel Accessor,
    MethodSignature<CliTypeIdentity> Signature,
    TypeDefinitionModel Owner);

internal abstract record UnsafeAccessorMemberMatch
{
    private UnsafeAccessorMemberMatch()
    {
    }

    internal sealed record Method(MethodDefinitionModel Definition) : UnsafeAccessorMemberMatch;
    internal sealed record Field(FieldDefinitionModel Definition) : UnsafeAccessorMemberMatch;
    internal sealed record Failure(UnsafeAccessorFailure Kind) : UnsafeAccessorMemberMatch;
}

internal enum UnsafeAccessorFailure
{
    BadImageFormat,
    MissingMethod,
    MissingField,
    AmbiguousMatch,
    InvalidProgram,
    TypeLoad,
}
