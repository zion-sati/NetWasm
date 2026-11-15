// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ITypedReferenceCallResolver
{
    // The TypedReference helper an instruction calls, over the instruction's type operand when it has one.
    MethodInstanceModel Resolve(TypedReferenceInstruction instruction, ImmutableArray<CliTypeIdentity> methodArguments);
}
