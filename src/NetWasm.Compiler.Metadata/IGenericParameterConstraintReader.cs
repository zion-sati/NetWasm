// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.GenericConstraints;

namespace NetWasm.Compiler.Metadata;

public interface IGenericParameterConstraintReader
{
    ImmutableArray<GenericParameterConstraintModel> Read(EntityKey owner);
}
