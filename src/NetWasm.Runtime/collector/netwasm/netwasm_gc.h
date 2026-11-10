/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GC_H
#define NETWASM_GC_H

#include "../collector_allocation.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/* Objects carry no collector header: the collector calls the runtime's
 * collector_object_layout directly whenever it needs an object's size or
 * references. */
typedef struct NetWasmGc NetWasmGc;

typedef void *(*NetWasmGcAllocate)(size_t bytes, size_t alignment);
typedef void (*NetWasmGcRelease)(void *address, size_t bytes, size_t alignment);
typedef void (*NetWasmGcTraceRoots)(NetWasmGc *gc, void *state);
typedef void (*NetWasmGcCompleteMark)(NetWasmGc *gc, void *state);

typedef enum {
    NETWASM_GC_OK,
    NETWASM_GC_INVALID_ARGUMENT,
    NETWASM_GC_SIZE_OVERFLOW,
    NETWASM_GC_OUT_OF_MEMORY,
    NETWASM_GC_UNKNOWN_OBJECT,
} NetWasmGcResult;

NetWasmGcResult netwasm_gc_create(
    NetWasmGcAllocate allocate,
    NetWasmGcRelease release,
    size_t minimum_collection_budget,
    NetWasmGc **result);

void netwasm_gc_set_root_tracer(
    NetWasmGc *gc,
    NetWasmGcTraceRoots trace_roots,
    void *state);

void netwasm_gc_set_mark_completion(
    NetWasmGc *gc,
    NetWasmGcCompleteMark complete_mark,
    void *state);

/* Allocations return zeroed storage. Only objects that contain references
 * are traced. */
#ifndef NETWASM_GC_COMPACT
/* Returns NULL without changing state when no precharged run is available.
 * Compact (compact_gc.c) has no fast path. */
void *netwasm_gc_try_allocate_available(
    NetWasmGc *gc,
    size_t bytes,
    bool contains_references);
#endif

NetWasmGcResult netwasm_gc_allocate(
    NetWasmGc *gc,
    size_t bytes,
    bool contains_references,
    void **result);

/* An open run of one size class: zeroed slots from cursor to end. */
typedef struct {
    unsigned char *cursor;
    unsigned char *end;
} NetWasmGcRun;

/* The collector's runs, indexed [slot_bytes / A - 1][contains_references],
 * where A is max_align_t's alignment and slot_bytes = (bytes + A) & ~(A - 1).
 * Code outside the collector may take an object of `bytes` (below 4 KiB) from
 * its run by advancing the cursor by slot_bytes while the cursor stays at or
 * below the end; the slot is zeroed and counted when the run closes. NULL when
 * the collector has no such runs (Compact). */
NetWasmGcRun *netwasm_gc_allocation_runs(NetWasmGc *gc);

void netwasm_gc_collect(NetWasmGc *gc);
bool netwasm_gc_mark(NetWasmGc *gc, uintptr_t address);
void netwasm_gc_drain(NetWasmGc *gc);
bool netwasm_gc_is_marked(const NetWasmGc *gc, uintptr_t address);
uintptr_t netwasm_gc_base(const NetWasmGc *gc, uintptr_t address);
bool netwasm_gc_contains(const NetWasmGc *gc, uintptr_t address);
bool netwasm_gc_is_collecting(const NetWasmGc *gc);

uint64_t netwasm_gc_collection_count(const NetWasmGc *gc);
uint64_t netwasm_gc_total_allocated_bytes(const NetWasmGc *gc);
uint64_t netwasm_gc_live_bytes(const NetWasmGc *gc);
uint64_t netwasm_gc_reserved_bytes(const NetWasmGc *gc);
size_t netwasm_gc_object_count(const NetWasmGc *gc);

void netwasm_gc_destroy(NetWasmGc *gc);

#ifdef NETWASM_GC_STATISTICS
/* Diagnostic-only allocator accounting. Shipped builds never define
 * NETWASM_GC_STATISTICS. Event counts are cumulative; the memory fields
 * describe the heap when read and partition it without double counting:
 * arena_bytes = (block_count + empty_block_count) * 64 KiB + unused_arena_bytes,
 * and each block's 64 KiB = metadata + tail + occupied + reserved + free. */
#define NETWASM_GC_STATISTICS_FIELDS(X) \
    X(fast_allocations) \
    X(small_allocations) \
    X(large_allocations) \
    X(run_refills) \
    X(run_slots) \
    X(run_bytes_zeroed) \
    X(free_run_searches) \
    X(bitmap_words_searched) \
    X(exhausted_blocks) \
    X(fresh_blocks) \
    X(recycled_blocks) \
    X(root_marks) \
    X(reference_marks) \
    X(reference_fallbacks) \
    X(marked_objects) \
    X(swept_blocks) \
    X(emptied_blocks) \
    X(freed_large_objects) \
    X(collections) \
    X(object_count) \
    X(large_object_count) \
    X(live_payload_bytes) \
    X(small_header_bytes) \
    X(small_padding_bytes) \
    X(small_occupied_bytes) \
    X(reserved_run_bytes) \
    X(free_slot_bytes) \
    X(block_count) \
    X(block_metadata_bytes) \
    X(block_tail_bytes) \
    X(empty_block_count) \
    X(unused_arena_bytes) \
    X(arena_bytes) \
    X(arena_header_bytes) \
    X(large_bytes) \
    X(directory_bytes)

typedef struct {
#define NETWASM_GC_STATISTICS_FIELD(name) uint64_t name;
    NETWASM_GC_STATISTICS_FIELDS(NETWASM_GC_STATISTICS_FIELD)
#undef NETWASM_GC_STATISTICS_FIELD
} NetWasmGcStatistics;

void netwasm_gc_read_statistics(const NetWasmGc *gc, NetWasmGcStatistics *result);
#endif

#endif
