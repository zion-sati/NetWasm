// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Emission writes no branch for endfinally: a finally or fault handler is left by falling out of its end, so its
// endfinally must end it (StructuredTargetValidator). Structuring a handler can place code after the block that ends
// with endfinally: a block that never returns and that several branches share becomes their join, which follows the
// labeled block around them, the endfinally included (a finally whose inlined Span<T>.CopyTo calls share their throw
// helper's call). Such a handler keeps its structure with its endfinally block moved to its end: the body becomes a
// labeled block whose next block is the endfinally block, each place that reached that block branches out of it, and
// the block's code follows. A handler whose endfinally already ends it is returned as it is.
internal static class EndFinallyPlacement
{
    public static StructuredSequenceDraft EndLast(StructuredSequenceDraft handler)
    {
        if (!HasMisplacedEndFinally(handler, ends: true))
        {
            return handler;
        }
        var endFinallyBlocks = new Dictionary<int, StructuredBlockDraft>();
        Collect(handler, endFinallyBlocks);
        // Only one block's code can come last; a handler with two endfinally blocks keeps its structure, which the
        // validator rejects with its own message.
        if (endFinallyBlocks.Count != 1)
        {
            return handler;
        }
        var (block, occurrence) = endFinallyBlocks.Single();
        return new([
            new StructuredLabeledBlockDraft(block, Redirect(handler, block)),
            occurrence with { IsOriginal = true },
        ]);
    }

    private static bool EndsWithEndFinally(StructuredBlockDraft block) =>
        block.Block.Terminator.Operation == CilOperation.EndFinally;

    // The validator's rule on a draft: an endfinally stands where falling out of it reaches the handler's end.
    private static bool HasMisplacedEndFinally(StructuredSequenceDraft sequence, bool ends)
    {
        for (var index = 0; index < sequence.Regions.Length; index++)
        {
            var last = ends && index == sequence.Regions.Length - 1;
            var misplaced = sequence.Regions[index] switch
            {
                StructuredBlockDraft block => !last && EndsWithEndFinally(block),
                StructuredSequenceDraft nested => HasMisplacedEndFinally(nested, last),
                StructuredIfDraft conditional => HasMisplacedEndFinally(conditional.WhenTrue, last) ||
                    HasMisplacedEndFinally(conditional.WhenFalse, last),
                StructuredLoopDraft loop => HasMisplacedEndFinally(loop.Body, ends: false) ||
                    HasMisplacedEndFinally(loop.ContinueBody, ends: false) ||
                    HasMisplacedEndFinally(loop.ExitBody, last),
                StructuredPostTestLoopDraft loop => HasMisplacedEndFinally(loop.Body, ends: false) ||
                    HasMisplacedEndFinally(loop.ContinueBody, ends: false) ||
                    HasMisplacedEndFinally(loop.ExitBody, last),
                StructuredLabeledBlockDraft labeled => HasMisplacedEndFinally(labeled.Body, ends: false),
                StructuredLabeledLoopDraft loop => HasMisplacedEndFinally(loop.Body, last),
                _ => false,
            };
            if (misplaced)
            {
                return true;
            }
        }
        return false;
    }

    // Every occurrence of a block that ends with endfinally, outside dispatchers (which branch out on their own) and
    // nested exception regions (whose handlers are their own).
    private static void Collect(StructuredSequenceDraft sequence, Dictionary<int, StructuredBlockDraft> blocks)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredBlockDraft block when EndsWithEndFinally(block):
                    blocks.TryAdd(block.Block.Index, block);
                    break;
                case StructuredSequenceDraft nested:
                    Collect(nested, blocks);
                    break;
                case StructuredIfDraft conditional:
                    Collect(conditional.WhenTrue, blocks);
                    Collect(conditional.WhenFalse, blocks);
                    break;
                case StructuredLoopDraft loop:
                    Collect(loop.Body, blocks);
                    Collect(loop.ContinueBody, blocks);
                    Collect(loop.ExitBody, blocks);
                    break;
                case StructuredPostTestLoopDraft loop:
                    Collect(loop.Body, blocks);
                    Collect(loop.ContinueBody, blocks);
                    Collect(loop.ExitBody, blocks);
                    break;
                case StructuredLabeledBlockDraft labeled:
                    Collect(labeled.Body, blocks);
                    break;
                case StructuredLabeledLoopDraft loop:
                    Collect(loop.Body, blocks);
                    break;
            }
        }
    }

    // The sequence with each occurrence of the endfinally block replaced by a branch to the labeled block that ends
    // where its code follows.
    private static StructuredSequenceDraft Redirect(StructuredSequenceDraft sequence, int block)
    {
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>(sequence.Regions.Length);
        foreach (var region in sequence.Regions)
        {
            regions.Add(region switch
            {
                StructuredBlockDraft code when code.Block.Index == block => new StructuredLabelBranchDraft(block),
                StructuredSequenceDraft nested => Redirect(nested, block),
                StructuredIfDraft conditional => conditional with
                {
                    WhenTrue = Redirect(conditional.WhenTrue, block),
                    WhenFalse = Redirect(conditional.WhenFalse, block),
                },
                StructuredLoopDraft loop => loop with
                {
                    Body = Redirect(loop.Body, block),
                    ContinueBody = Redirect(loop.ContinueBody, block),
                    ExitBody = Redirect(loop.ExitBody, block),
                },
                StructuredPostTestLoopDraft loop => loop with
                {
                    Body = Redirect(loop.Body, block),
                    ContinueBody = Redirect(loop.ContinueBody, block),
                    ExitBody = Redirect(loop.ExitBody, block),
                },
                StructuredLabeledBlockDraft labeled => labeled with { Body = Redirect(labeled.Body, block) },
                StructuredLabeledLoopDraft loop => loop with { Body = Redirect(loop.Body, block) },
                _ => region,
            });
        }
        return new(regions.MoveToImmutable());
    }
}
