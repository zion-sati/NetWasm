// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record RootMapAnalysisRequest(
    ManagedMethodBody Method,
    IMembership<EntityKey> AllocatingMethods,
    IMembership<string> AllocatingConstructedMethods);
