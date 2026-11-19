// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

// One run-time type whose attributes answer the query, and the selection planned for it.
internal sealed record RuntimeTypeAttributeCase(CliTypeIdentity Type, AttributeQueryEntry Selection);

// A query whose target is a Type known only at run time: each case is a run-time type of the program that carries the
// filter attribute, and Otherwise answers for every other type. A null target throws NullReferenceException for an
// instance call, or ArgumentNullException naming NullArgumentName for Attribute's statics and the extensions.
internal sealed record RuntimeTypeAttributeDispatchRequest(
    AttributeQueryCall Call,
    ImmutableArray<CliTypeIdentity> ArgumentTypes,
    ImmutableArray<RuntimeTypeAttributeCase> Cases,
    AttributeQueryEntry Otherwise,
    bool? InheritConstant,
    CliTypeIdentity ResultArrayType,
    MethodInstanceModel? AmbiguityConstructor,
    MethodInstanceModel NullTargetException,
    string? NullArgumentName,
    int FirstLocal);
