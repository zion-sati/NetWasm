// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// An instruction of a reached body that names an attribute query API: a call, a delegate's or function pointer's
// ldftn, or a method handle's ldtoken. A call the classifier takes carries its classification, and, where the body's
// code reaches it, the proofs of its arguments.
internal sealed record AttributeQueryReference(
    CilInstruction Instruction,
    MethodInstanceModel Method,
    AttributeQueryCall? Call,
    ImmutableArray<AttributeValueProof>? Stack);
