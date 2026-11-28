// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Part of a method structured on its own: a protected block's or a handler's blocks, entered at Entry. An edge that
// leaves the blocks (a protected block's leave) ends its block, as a return does.
internal sealed record StructuringRange(int Entry, ImmutableHashSet<int> Blocks);
