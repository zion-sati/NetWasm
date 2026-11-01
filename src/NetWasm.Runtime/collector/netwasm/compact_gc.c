/* Compact collector for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
/* Compact is NetWasm's size-first collector: a non-moving mark-sweep behind
 * NetWasmGC's interface (netwasm_gc.h), so the two share their runtime
 * adapters and lifetime layer. Small objects are packed into 64 KiB chunks,
 * each behind a one-word header holding its slot size and its mark, traced and
 * free flags. A bitmap per chunk records where objects start, so any address
 * resolves to its object. Freed slots go to free lists by size; objects over
 * 4 KiB are allocated singly. Marking uses a stack that grows on demand, and
 * rescans the heap for marked objects if it cannot grow. */
#include "netwasm_gc.h"

#include <stdalign.h>
#include <string.h>

#ifndef NETWASM_GC_COMPACT
#error "compact_gc.c builds the Compact collector; define NETWASM_GC_COMPACT."
#endif
#ifdef NETWASM_GC_STATISTICS
#error "NetWasmGC statistics describe its blocks; Compact has none."
#endif

#define CHUNK_BYTES (64u * 1024u)
#define SMALL_LIMIT (4u * 1024u)
#define ALIGNMENT alignof(max_align_t)
#define GRANULES (CHUNK_BYTES / ALIGNMENT)
#define WORD_BITS (sizeof(uintptr_t) * 8u)
#define HEADER sizeof(size_t)
/* Header flags share the slot size's word; sizes are multiples of ALIGNMENT. */
#define MARKED ((size_t)1)
#define TRACED ((size_t)2)
#define FREE ((size_t)4)
#define FLAGS (MARKED | TRACED | FREE)
_Static_assert(ALIGNMENT > FLAGS && ALIGNMENT >= HEADER, "the header must fit a granule and leave flag bits");

/* A chunk's data, or a large object's payload, in address order. */
typedef struct {
    uintptr_t start;
    uintptr_t end;
    bool large;
} Area;

typedef struct {
    Area area;
    unsigned char *cursor;
    uintptr_t starts[GRANULES / WORD_BITS];
    max_align_t alignment;
    unsigned char data[];
} Chunk;

typedef struct {
    Area area;
    size_t allocation_bytes;
    max_align_t alignment;
    unsigned char data[];
} Large;

struct NetWasmGc {
    NetWasmGcAllocate allocate;
    NetWasmGcRelease release;
    NetWasmGcTraceRoots trace_roots;
    void *root_state;
    NetWasmGcCompleteMark complete_mark;
    void *mark_state;
    Area **areas;
    size_t area_count;
    size_t area_capacity;
    Chunk *current;
    unsigned char *free[SMALL_LIMIT / ALIGNMENT];
    unsigned char **stack;
    size_t stack_count;
    size_t stack_capacity;
    size_t object_count;
    size_t occupied_bytes;
    size_t backing_bytes;
    size_t minimum_collection_budget;
    size_t next_collection;
    uint64_t collections;
    uint64_t allocated_bytes;
    bool collecting;
    bool overflowed;
};

static size_t *header(unsigned char *object) { return (size_t *)(void *)object - 1; }

static size_t highest_bit(uintptr_t bits)
{
#if UINTPTR_MAX == UINT32_MAX
    return 31u - (size_t)__builtin_clz((unsigned)bits);
#else
    return 63u - (size_t)__builtin_clzll((unsigned long long)bits);
#endif
}

static void set_start(Chunk *chunk, unsigned char *object, bool started)
{
    size_t granule = (size_t)(object - chunk->data) / ALIGNMENT;
    uintptr_t bit = (uintptr_t)1 << (granule % WORD_BITS);
    if (started) chunk->starts[granule / WORD_BITS] |= bit;
    else chunk->starts[granule / WORD_BITS] &= ~bit;
}

/* The object a precise address names: its start, inside it, or one past it. */
static unsigned char *resolve_in(const Area *area, uintptr_t address)
{
    unsigned char *object;
    size_t bytes;
    if (area->large) {
        object = ((Large *)(uintptr_t)area)->data + ALIGNMENT;
        bytes = (size_t)(area->end - area->start);
    } else {
        const Chunk *chunk = (const Chunk *)area;
        size_t granule = (size_t)(address - area->start) / ALIGNMENT;
        if (granule >= GRANULES) granule = GRANULES - 1;
        size_t word = granule / WORD_BITS;
        uintptr_t bits = chunk->starts[word] & (UINTPTR_MAX >> (WORD_BITS - 1 - granule % WORD_BITS));
        while (bits == 0) {
            if (word == 0) return NULL;
            bits = chunk->starts[--word];
        }
        object = (unsigned char *)chunk->data + (word * WORD_BITS + highest_bit(bits)) * ALIGNMENT;
        if ((uintptr_t)object == address) return object;
        NetWasmCollectorLayout layout;
        collector_object_layout(object, &layout);
        bytes = layout.fixed_bytes + layout.element_count * layout.element_size;
    }
    uintptr_t first = (uintptr_t)object;
    return address >= first && address - first <= bytes ? object : NULL;
}

/* The number of areas that start at or before address. */
static size_t areas_before(const NetWasmGc *gc, uintptr_t address)
{
    size_t low = 0, high = gc->area_count;
    while (low < high) {
        size_t middle = low + (high - low) / 2;
        if (gc->areas[middle]->start <= address) low = middle + 1;
        else high = middle;
    }
    return low;
}

static unsigned char *resolve(const NetWasmGc *gc, uintptr_t address)
{
    size_t low = areas_before(gc, address);
    /* An object's end can be the next area's start. */
    for (size_t index = low; index > 0 && index + 2 > low; index--) {
        const Area *area = gc->areas[index - 1];
        if (address > area->end) return NULL;
        unsigned char *object = resolve_in(area, address);
        if (object != NULL) return object;
    }
    return NULL;
}

static bool insert_area(NetWasmGc *gc, Area *area)
{
    if (gc->area_count == gc->area_capacity) {
        size_t capacity = gc->area_capacity == 0 ? 16 : gc->area_capacity * 2;
        if (capacity > SIZE_MAX / sizeof(Area *)) return false;
        Area **areas = gc->allocate(capacity * sizeof(Area *), alignof(Area *));
        if (areas == NULL) return false;
        if (gc->areas != NULL) {
            memcpy(areas, gc->areas, gc->area_count * sizeof(Area *));
            gc->release(gc->areas, gc->area_capacity * sizeof(Area *), alignof(Area *));
        }
        gc->areas = areas;
        gc->area_capacity = capacity;
    }
    size_t index = gc->area_count;
    while (index > 0 && gc->areas[index - 1]->start > area->start) {
        gc->areas[index] = gc->areas[index - 1];
        index--;
    }
    gc->areas[index] = area;
    gc->area_count++;
    return true;
}

static unsigned char *allocate_small(NetWasmGc *gc, size_t size)
{
    unsigned char **free = &gc->free[size / ALIGNMENT - 1];
    unsigned char *object = *free;
    Chunk *chunk;
    if (object != NULL) {
        memcpy(free, object, sizeof(*free));
        chunk = (Chunk *)gc->areas[areas_before(gc, (uintptr_t)object) - 1];
    } else {
        chunk = gc->current;
        if (chunk == NULL || chunk->cursor + size > chunk->data + CHUNK_BYTES) {
            chunk = gc->allocate(sizeof(Chunk) + CHUNK_BYTES, ALIGNMENT);
            if (chunk == NULL) return NULL;
            memset(chunk, 0, sizeof(Chunk));
            chunk->area.start = (uintptr_t)chunk->data;
            chunk->area.end = (uintptr_t)chunk->data + CHUNK_BYTES;
            chunk->cursor = chunk->data + ALIGNMENT - HEADER;
            if (!insert_area(gc, &chunk->area)) {
                gc->release(chunk, sizeof(Chunk) + CHUNK_BYTES, ALIGNMENT);
                return NULL;
            }
            gc->backing_bytes += sizeof(Chunk) + CHUNK_BYTES;
            gc->current = chunk;
        }
        object = chunk->cursor + HEADER;
        chunk->cursor += size;
    }
    set_start(chunk, object, true);
    memset(object, 0, size - HEADER);
    return object;
}

static unsigned char *allocate_large(NetWasmGc *gc, size_t bytes, size_t *size)
{
    size_t allocation_bytes = (sizeof(Large) + ALIGNMENT + bytes + ALIGNMENT - 1) & ~(ALIGNMENT - 1);
    Large *large = gc->allocate(allocation_bytes, ALIGNMENT);
    if (large == NULL) return NULL;
    unsigned char *object = large->data + ALIGNMENT;
    large->area = (Area){(uintptr_t)object, (uintptr_t)object + bytes, true};
    large->allocation_bytes = allocation_bytes;
    if (!insert_area(gc, &large->area)) {
        gc->release(large, allocation_bytes, ALIGNMENT);
        return NULL;
    }
    gc->backing_bytes += allocation_bytes;
    *size = allocation_bytes;
    memset(object, 0, bytes);
    return object;
}

NetWasmGcResult netwasm_gc_allocate(
    NetWasmGc *gc,
    size_t bytes,
    bool contains_references,
    void **result)
{
    if (gc == NULL || result == NULL || gc->collecting) return NETWASM_GC_INVALID_ARGUMENT;
    if (bytes > SIZE_MAX - HEADER - ALIGNMENT * 2 - sizeof(Large)) return NETWASM_GC_SIZE_OVERFLOW;
    size_t size = (HEADER + (bytes == 0 ? 1 : bytes) + ALIGNMENT - 1) & ~(ALIGNMENT - 1);
#ifdef NETWASM_GC_STRESS
    // Qualification only: collect before every allocation, so a reference
    // held without a root is freed at the first chance.
    if (gc->object_count != 0) netwasm_gc_collect(gc);
#else
    if ((size >= gc->next_collection || gc->occupied_bytes >= gc->next_collection - size) &&
        gc->object_count != 0) netwasm_gc_collect(gc);
#endif
    unsigned char *object = size <= SMALL_LIMIT
        ? allocate_small(gc, size) : allocate_large(gc, bytes, &size);
    if (object == NULL) return NETWASM_GC_OUT_OF_MEMORY;
    *header(object) = size | (contains_references ? TRACED : 0);
    gc->object_count++;
    gc->occupied_bytes += size;
    gc->allocated_bytes += bytes;
    *result = object;
    return NETWASM_GC_OK;
}

static void push(NetWasmGc *gc, unsigned char *object)
{
    if (gc->stack_count == gc->stack_capacity) {
        size_t capacity = gc->stack_capacity == 0 ? 256 : gc->stack_capacity * 2;
        unsigned char **stack = capacity <= SIZE_MAX / sizeof(*stack)
            ? gc->allocate(capacity * sizeof(*stack), alignof(unsigned char *)) : NULL;
        /* Marked but untraced objects are found again by the rescan. */
        if (stack == NULL) {
            gc->overflowed = true;
            return;
        }
        if (gc->stack != NULL) {
            memcpy(stack, gc->stack, gc->stack_count * sizeof(*stack));
            gc->release(gc->stack, gc->stack_capacity * sizeof(*stack), alignof(unsigned char *));
        }
        gc->stack = stack;
        gc->stack_capacity = capacity;
    }
    gc->stack[gc->stack_count++] = object;
}

static bool mark_object(NetWasmGc *gc, unsigned char *object)
{
    if (object == NULL || (*header(object) & MARKED) != 0) return false;
    *header(object) |= MARKED;
    if ((*header(object) & TRACED) != 0) push(gc, object);
    return true;
}

static void mark_slot(NetWasmGc *gc, const unsigned char *slot)
{
    uintptr_t reference;
    /* Packed reference fields need not be pointer-aligned. */
    memcpy(&reference, slot, sizeof(reference));
    if (reference != 0) mark_object(gc, resolve(gc, reference));
}

static void trace(NetWasmGc *gc, unsigned char *object)
{
    NetWasmCollectorLayout layout;
    collector_object_layout(object, &layout);
    for (size_t index = 0; index < layout.fixed_reference_count; index++)
        mark_slot(gc, object + layout.fixed_offsets[index]);
    if (layout.element_reference_count == 0) return;
    const unsigned char *element = object + layout.fixed_bytes;
    for (size_t count = 0; count < layout.element_count; count++, element += layout.element_size)
        for (size_t index = 0; index < layout.element_reference_count; index++)
            mark_slot(gc, element + layout.element_offsets[index]);
}

bool netwasm_gc_mark(NetWasmGc *gc, uintptr_t address)
{
    return gc != NULL && gc->collecting && address != 0 && mark_object(gc, resolve(gc, address));
}

void netwasm_gc_drain(NetWasmGc *gc)
{
    if (gc == NULL || !gc->collecting) return;
    for (;;) {
        while (gc->stack_count != 0) trace(gc, gc->stack[--gc->stack_count]);
        if (!gc->overflowed) return;
        gc->overflowed = false;
        for (size_t index = 0; index < gc->area_count; index++) {
            Area *area = gc->areas[index];
            if (area->large) {
                unsigned char *object = ((Large *)area)->data + ALIGNMENT;
                if ((*header(object) & (MARKED | TRACED)) == (MARKED | TRACED)) trace(gc, object);
                continue;
            }
            Chunk *chunk = (Chunk *)area;
            for (unsigned char *slot = chunk->data + ALIGNMENT - HEADER; slot < chunk->cursor;) {
                size_t word = *(size_t *)(void *)slot;
                if ((word & (MARKED | TRACED | FREE)) == (MARKED | TRACED)) trace(gc, slot + HEADER);
                slot += word & ~FLAGS;
            }
        }
    }
}

static void sweep(NetWasmGc *gc)
{
    gc->object_count = 0;
    gc->occupied_bytes = 0;
    size_t kept = 0;
    for (size_t index = 0; index < gc->area_count; index++) {
        Area *area = gc->areas[index];
        if (area->large) {
            Large *large = (Large *)area;
            size_t *word = header(large->data + ALIGNMENT);
            if ((*word & MARKED) == 0) {
                gc->backing_bytes -= large->allocation_bytes;
                gc->release(large, large->allocation_bytes, ALIGNMENT);
                continue;
            }
            *word &= ~MARKED;
            gc->object_count++;
            gc->occupied_bytes += large->allocation_bytes;
        } else {
            Chunk *chunk = (Chunk *)area;
            for (unsigned char *slot = chunk->data + ALIGNMENT - HEADER; slot < chunk->cursor;) {
                size_t *word = (size_t *)(void *)slot;
                size_t size = *word & ~FLAGS;
                if ((*word & MARKED) != 0) {
                    *word &= ~MARKED;
                    gc->object_count++;
                    gc->occupied_bytes += size;
                } else if ((*word & FREE) == 0) {
                    unsigned char *object = slot + HEADER;
                    *word = size | FREE;
                    set_start(chunk, object, false);
                    memcpy(object, &gc->free[size / ALIGNMENT - 1], sizeof(unsigned char *));
#ifdef NETWASM_GC_STRESS
                    // A freed object reads as garbage, not as itself, until
                    // allocation zeroes its slot again.
                    memset(object + sizeof(unsigned char *), 0xA5, size - HEADER - sizeof(unsigned char *));
#endif
                    gc->free[size / ALIGNMENT - 1] = object;
                }
                slot += size;
            }
        }
        gc->areas[kept++] = area;
    }
    gc->area_count = kept;
    gc->next_collection = gc->occupied_bytes > (SIZE_MAX - gc->minimum_collection_budget) / 2
        ? SIZE_MAX : gc->occupied_bytes * 2 + gc->minimum_collection_budget;
}

void netwasm_gc_collect(NetWasmGc *gc)
{
    if (gc == NULL || gc->collecting) return;
    gc->collecting = true;
    if (gc->trace_roots != NULL) gc->trace_roots(gc, gc->root_state);
    netwasm_gc_drain(gc);
    if (gc->complete_mark != NULL) gc->complete_mark(gc, gc->mark_state);
    netwasm_gc_drain(gc);
    sweep(gc);
    gc->collections++;
    gc->collecting = false;
}

NetWasmGcResult netwasm_gc_create(
    NetWasmGcAllocate allocate,
    NetWasmGcRelease release,
    size_t minimum_collection_budget,
    NetWasmGc **result)
{
    if (allocate == NULL || release == NULL || result == NULL ||
        minimum_collection_budget == 0)
        return NETWASM_GC_INVALID_ARGUMENT;
    NetWasmGc *gc = allocate(sizeof(*gc), alignof(NetWasmGc));
    if (gc == NULL) return NETWASM_GC_OUT_OF_MEMORY;
    memset(gc, 0, sizeof(*gc));
    gc->allocate = allocate;
    gc->release = release;
    gc->minimum_collection_budget = minimum_collection_budget;
    gc->next_collection = minimum_collection_budget;
    *result = gc;
    return NETWASM_GC_OK;
}

void netwasm_gc_set_root_tracer(NetWasmGc *gc, NetWasmGcTraceRoots trace_roots, void *state)
{
    gc->trace_roots = trace_roots;
    gc->root_state = state;
}

void netwasm_gc_set_mark_completion(NetWasmGc *gc, NetWasmGcCompleteMark complete_mark, void *state)
{
    gc->complete_mark = complete_mark;
    gc->mark_state = state;
}

bool netwasm_gc_is_marked(const NetWasmGc *gc, uintptr_t address)
{
    unsigned char *object = gc == NULL || address == 0 ? NULL : resolve(gc, address);
    return object != NULL && (*header(object) & MARKED) != 0;
}

uintptr_t netwasm_gc_base(const NetWasmGc *gc, uintptr_t address)
{
    return gc == NULL || address == 0 ? 0 : (uintptr_t)resolve(gc, address);
}

bool netwasm_gc_contains(const NetWasmGc *gc, uintptr_t address)
{
    return netwasm_gc_base(gc, address) != 0;
}

bool netwasm_gc_is_collecting(const NetWasmGc *gc) { return gc != NULL && gc->collecting; }
uint64_t netwasm_gc_collection_count(const NetWasmGc *gc) { return gc == NULL ? 0 : gc->collections; }
uint64_t netwasm_gc_total_allocated_bytes(const NetWasmGc *gc) { return gc == NULL ? 0 : gc->allocated_bytes; }
uint64_t netwasm_gc_live_bytes(const NetWasmGc *gc) { return gc == NULL ? 0 : gc->occupied_bytes; }
uint64_t netwasm_gc_reserved_bytes(const NetWasmGc *gc) { return gc == NULL ? 0 : gc->backing_bytes; }
size_t netwasm_gc_object_count(const NetWasmGc *gc) { return gc == NULL ? 0 : gc->object_count; }

void netwasm_gc_destroy(NetWasmGc *gc)
{
    if (gc == NULL) return;
    for (size_t index = 0; index < gc->area_count; index++) {
        Area *area = gc->areas[index];
        if (area->large) gc->release(area, ((Large *)area)->allocation_bytes, ALIGNMENT);
        else gc->release(area, sizeof(Chunk) + CHUNK_BYTES, ALIGNMENT);
    }
    if (gc->areas != NULL) gc->release(gc->areas, gc->area_capacity * sizeof(Area *), alignof(Area *));
    if (gc->stack != NULL)
        gc->release(gc->stack, gc->stack_capacity * sizeof(*gc->stack), alignof(unsigned char *));
    NetWasmGcRelease release = gc->release;
    release(gc, sizeof(*gc), alignof(NetWasmGc));
}
