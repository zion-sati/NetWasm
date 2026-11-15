// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;

// The empty sequence fixtures start from; the structurer builds its own. In the
// global namespace, so tests that name the structured model through an alias
// see it too.
internal static class StructuredSequenceFixtures
{
    private static readonly StructuredSequence EmptySequence = new([]);

    extension(StructuredSequence)
    {
        public static StructuredSequence Empty => EmptySequence;
    }
}
