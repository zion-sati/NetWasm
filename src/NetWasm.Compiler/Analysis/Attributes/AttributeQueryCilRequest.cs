// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal enum AttributeQueryOperation
{
    IsDefined,
    GetOne,
    GetMany,
}

internal sealed record AttributeQueryCilRequest(
    AttributeQueryOperation Operation,
    AttributeQueryEntry Selection,
    int ArgumentCount,
    int? InheritArgument,
    bool? InheritConstant,
    CliTypeIdentity ResultArrayType,
    MethodInstanceModel? AmbiguityConstructor,
    int FirstLocal);
