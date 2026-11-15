// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

public sealed record ManagedCallSite(
    ManagedCallSiteKey Key,
    ManagedCallOperation Operation,
    ManagedMethodIdentity TargetIdentity,
    MethodInstanceModel Target,
    CliTypeIdentity? ConstrainedType);
