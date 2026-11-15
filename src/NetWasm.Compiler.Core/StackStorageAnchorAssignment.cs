// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core;

public abstract record StackStorageAnchorAssignment(int Destination)
{
    public sealed record Capture(int Anchor, int EvaluationStackSlot) : StackStorageAnchorAssignment(Anchor);

    public sealed record Copy(int Anchor, int Source) : StackStorageAnchorAssignment(Anchor);

    public sealed record Clear(int Anchor) : StackStorageAnchorAssignment(Anchor);
}
