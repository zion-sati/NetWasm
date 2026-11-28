// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// A slot use: the method a dispatch site names, by canonical name, with the operation that calls it. Every site of a
// slot use reaches the same target for the same receiver, so the closure resolves each (slot use, receiver) once
// however many sites call it; a site's receiver bound only selects which of the slot use's targets it reaches.
internal readonly record struct DispatchSlot(CilOperation Operation, string Declaration);

// A receiver to resolve for a slot use. Declaration is the slot use's first site, which names its method and operation.
internal readonly record struct DispatchCandidate(
    DispatchSlot Slot,
    DispatchDeclaration Declaration,
    CliTypeIdentity Receiver);

internal readonly record struct DispatchDeclarationCandidate(
    string DispatchKey,
    DispatchDeclaration Declaration);

internal readonly record struct DispatchReceiverCandidate(CliTypeIdentity Receiver);
