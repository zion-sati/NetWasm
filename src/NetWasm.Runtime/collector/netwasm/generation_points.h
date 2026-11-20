/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GC_GENERATION_POINTS_H
#define NETWASM_GC_GENERATION_POINTS_H

/* The points at which generations change what NetWasmGC does, and what it answers about them. netwasm_gc.c
 * comes to each of them whether it has generations or not, and knows nothing else about them.
 *
 * generational_gc.c is NetWasmGC with generations: it declares the points, includes netwasm_gc.c and defines
 * them after it. Compiled on its own, netwasm_gc.c has none, and each point is what is written here: nothing,
 * or the collector's answer without generations. They are macros, so that a collector without generations is
 * compiled exactly as if the points were not there.
 *
 * generations_begin(gc)
 *     The collector was created. An answer other than NETWASM_GC_OK refuses it.
 * generations_end(gc)
 *     The collector is being destroyed.
 * generations_cover(gc, start, end)
 *     The collector took the memory [start, end) for objects. An answer other than NETWASM_GC_OK refuses it.
 * generations_collect_due(gc)
 *     An allocation, or an idle embedder, asks for the collection the heap is heading for.
 * generations_collect_young(gc)
 *     GC.Collect(0): a collection of the youngest generation, which is every object without generations.
 * generations_collect_all(gc)
 *     netwasm_gc_collect is about to mark: a collection of every object.
 * generations_may_mark(gc)
 *     Whether the mark may go on to its next block of pending objects.
 * generations_keep_marks
 *     Whether a collection's survivors stay marked after its sweep.
 * generations_forget_writes(traced, start, end)
 *     The sweep has gone over the objects in [start, end), which hold references when traced is set.
 * generations_note_allocation(block)
 *     A run was opened in the block: it holds objects allocated since the last collection.
 * generations_note_large(large)
 *     The large object was allocated.
 * generations_block_unchanged(gc, block)
 *     Whether the sweep under way may leave the block as it stands: a minor collection's, of a block nothing was
 *     allocated into since the last collection, whose cards the collection cleared or found clear.
 * generations_large_unchanged(gc, large)
 *     Whether the sweep under way may leave the cards of the old large object as they are.
 * generations_note_swept(block), generations_note_swept_large(large)
 *     The sweep has gone over the block or kept the large object.
 * generations_pace(gc, charge)
 *     A sweep is over, and the pacing rule makes the next collection of every object due at the charge. Answers
 *     when the next collection is due.
 * generations_minor_due(gc)
 *     Whether the collection the heap is heading for is a minor one, which costs what the young objects hold
 *     rather than what the heap does.
 * generations_minor_collections(gc)
 *     How many of the collections that ran to their end were minor ones.
 * generations_of(gc, address)
 *     The generation of the object at the address, which is not null. */
#ifndef NWGC_GENERATION_POINTS

#define generations_begin(gc) NETWASM_GC_OK
#define generations_end(gc) ((void)0)
#define generations_cover(gc, start, end) NETWASM_GC_OK
#define generations_collect_due(gc) netwasm_gc_collect(gc)
#define generations_collect_young(gc) netwasm_gc_collect(gc)
#define generations_collect_all(gc) ((void)0)
#define generations_may_mark(gc) true
#define generations_keep_marks false
#define generations_forget_writes(traced, start, end) ((void)0)
#define generations_note_allocation(block) ((void)0)
#define generations_note_large(large) ((void)0)
#define generations_block_unchanged(gc, block) false
#define generations_large_unchanged(gc, large) false
#define generations_note_swept(block) ((void)0)
#define generations_note_swept_large(large) ((void)0)
#define generations_pace(gc, charge) (charge)
#define generations_minor_due(gc) false
#define generations_minor_collections(gc) ((uint64_t)0)
#define generations_of(gc, address) ((void)(address), 0u)

#endif
#endif
