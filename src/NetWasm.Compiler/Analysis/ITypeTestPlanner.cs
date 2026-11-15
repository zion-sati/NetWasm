// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ITypeTestPlanner
{
    ImmutableDictionary<string, TypeTestSiteModel> Build(
        IEnumerable<ManagedMethodBody> methods,
        IEnumerable<CliTypeIdentity> allocatedTypes);
}
