// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// Walks a structured method's regions.
internal static class StructuredTree
{
    // How deep the sequence's regions nest.
    public static int Depth(StructuredSequence sequence)
    {
        var depth = 0;
        foreach (var region in sequence.Regions)
        {
            foreach (var child in Children(region))
            {
                depth = Math.Max(depth, 1 + Depth(child));
            }
        }
        return depth;
    }

    public static IEnumerable<StructuredRegion> Descendants(StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            yield return region;
            foreach (var child in Children(region).SelectMany(Descendants))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<StructuredSequence> Children(StructuredRegion region) => region switch
    {
        StructuredIf conditional => [conditional.WhenTrue, conditional.WhenFalse],
        StructuredLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
        StructuredPostTestLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
        StructuredLabeledBlock labeled => [labeled.Body],
        StructuredLabeledLoop labeled => [labeled.Body],
        _ => [],
    };
}
