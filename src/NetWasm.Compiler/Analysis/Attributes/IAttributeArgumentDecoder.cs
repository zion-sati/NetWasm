// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeArgumentDecoder
{
    AttributeArgumentPlan Decode(CustomAttributeTypedArgument<CliTypeIdentity> argument,
        CliTypeIdentity destination);
}
