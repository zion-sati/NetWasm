// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.DynamicCasts;

// A method of CoreLib's DynamicInterfaceCastableHelpers, instantiated with the given method arguments.
internal interface IDynamicCastHelperResolver
{
    MethodInstanceModel Resolve(string name, ImmutableArray<CliTypeIdentity> methodArguments);
}
