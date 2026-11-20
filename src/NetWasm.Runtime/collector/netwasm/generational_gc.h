/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GENERATIONAL_GC_H
#define NETWASM_GENERATIONAL_GC_H

#include "netwasm_gc.h"

/* NetWasmGC with generations (generational_gc.c) is NetWasmGC, with netwasm_gc.h's interface, and these
 * differences:
 *
 * - A collection keeps its survivors marked, so netwasm_gc_is_marked tells an object that has survived a
 *   collection from one allocated since.
 * - netwasm_gc_collect collects every object. The collections an allocation or an idle embedder asks for are
 *   minor ones while those pay: they free only objects allocated since the last collection.
 * - Every store of a reference into one of its objects is recorded with netwasm_gc_record_reference_write
 *   (reference_cards.h); a minor collection finds an old object's young ones by those records alone.
 * - One such collector exists at a time: creating a second fails with NETWASM_GC_INVALID_ARGUMENT.
 *
 * netwasm_gc_collection_count counts every collection that ran to its end. Of those, this counts the minor
 * ones. */
uint64_t netwasm_gc_minor_collection_count(const NetWasmGc *gc);
/* The minor collections given up because too much of what they looked at was alive. */
uint64_t netwasm_gc_given_up_minor_collection_count(const NetWasmGc *gc);

/* Implemented by the embedder, for qualification builds (NETWASM_GC_STRESS) only, which check every minor
 * collection: called for a reference an old object holds to a young object that no recorded store accounts for,
 * with the holder, the slot and the reference. The collection goes on when it returns. */
void collector_missed_reference_write(const void *holder, const void *slot, uintptr_t reference);

#endif
