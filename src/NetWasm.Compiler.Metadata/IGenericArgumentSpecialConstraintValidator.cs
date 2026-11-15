// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record GenericArgumentSpecialConstraintRequest(
    CliTypeIdentity Argument, GenericParameterAttributes Attributes, string Caller, int IlOffset);

public interface IGenericArgumentSpecialConstraintValidator
{
    void Validate(GenericArgumentSpecialConstraintRequest request);
}
