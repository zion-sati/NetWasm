/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "netwasm_gc.h"

#include <limits.h>
#include <stdalign.h>
#include <stdlib.h>
#include <string.h>

#define NWGC_PAGE_SHIFT 12u
#define NWGC_PAGE_BYTES ((size_t)1u << NWGC_PAGE_SHIFT)
#define NWGC_BLOCK_BYTES (64u * 1024u)
#define NWGC_SMALL_LIMIT (4u * 1024u)
#define NWGC_RUN_BYTES (4u * 1024u)
#define NWGC_ALIGNMENT alignof(max_align_t)
#define NWGC_WORD_BITS (sizeof(uintptr_t) * CHAR_BIT)
#define NWGC_CLASS_COUNT (NWGC_SMALL_LIMIT / NWGC_ALIGNMENT)
#if UINTPTR_MAX <= UINT32_MAX
#define NWGC_DIRECTORY_BITS 10u
#define NWGC_DIRECTORY_LEVELS 2u
#else
#define NWGC_DIRECTORY_BITS 8u
#define NWGC_DIRECTORY_LEVELS 7u
#endif
#ifdef NETWASM_GC_STATISTICS
#define NWGC_COUNT(gc, field, amount) ((gc)->statistics.field += (amount))
#else
#define NWGC_COUNT(gc, field, amount) ((void)0)
#endif
#define NWGC_DIRECTORY_FANOUT ((size_t)1u << NWGC_DIRECTORY_BITS)
#define NWGC_DIRECTORY_MASK (NWGC_DIRECTORY_FANOUT - 1)

typedef enum {
    NWGC_REGION_BLOCK,
    NWGC_REGION_LARGE,
} NetWasmGcRegionKind;

typedef struct NetWasmGcRegion {
    uintptr_t start;
    uintptr_t end;
    NetWasmGcRegionKind kind;
} NetWasmGcRegion;

typedef struct NetWasmGcBlock NetWasmGcBlock;
typedef struct NetWasmGcLarge NetWasmGcLarge;
typedef struct NetWasmGcArena NetWasmGcArena;
typedef struct NetWasmGcDirectoryAllocation NetWasmGcDirectoryAllocation;
typedef struct NetWasmGcDirectoryNode NetWasmGcDirectoryNode;
typedef struct NetWasmGcDirectoryLeaf NetWasmGcDirectoryLeaf;

/* A run is a stretch of a block's slots reserved, zeroed and charged to the
 * heap at once, then bump-allocated. Its objects are counted when it closes. */
typedef struct {
    NetWasmGcBlock *run_block;
    unsigned char *run_start;
    unsigned char *run_cursor;
    unsigned char *run_end;
    NetWasmGcBlock *available;
} NetWasmGcClass;

struct NetWasmGcBlock {
    NetWasmGcRegion region;
    NetWasmGcBlock *next;
    NetWasmGcBlock *available_next;
    NetWasmGcBlock *pending_next;
    size_t slot_bytes;
    size_t slot_count;
    size_t bitmap_words;
    size_t slots_offset;
    size_t live;
    size_t search;
    size_t class_index;
    size_t pending_first;
    bool available;
    bool pending_queued;
    /* Every object in the block contains references and is traced. */
    bool traced;
};

struct NetWasmGcLarge {
    NetWasmGcRegion region;
    NetWasmGcLarge *next;
    NetWasmGcLarge *pending_next;
    bool marked;
    bool pending_queued;
    bool traced;
    size_t allocation_bytes;
    size_t payload_bytes;
    /* The object follows, aligned like any allocation. */
    max_align_t object[];
};

struct NetWasmGcArena {
    NetWasmGcArena *next;
    size_t allocation_bytes;
    size_t capacity;
    size_t used;
    max_align_t alignment;
    unsigned char data[];
};

typedef struct {
    NetWasmGcRegion *regions[2];
} NetWasmGcPage;

struct NetWasmGcDirectoryAllocation {
    NetWasmGcDirectoryAllocation *next;
    size_t bytes;
};

struct NetWasmGcDirectoryNode {
    NetWasmGcDirectoryAllocation allocation;
    void *entries[NWGC_DIRECTORY_FANOUT];
};

struct NetWasmGcDirectoryLeaf {
    NetWasmGcDirectoryAllocation allocation;
    NetWasmGcPage pages[NWGC_DIRECTORY_FANOUT];
};

struct NetWasmGc {
    NetWasmGcAllocate allocate;
    NetWasmGcRelease release;
    NetWasmGcTraceRoots trace_roots;
    void *root_state;
    NetWasmGcCompleteMark complete_mark;
    void *mark_state;
    NetWasmGcBlock *blocks;
    NetWasmGcBlock *empty_blocks;
    NetWasmGcBlock *pending_blocks;
    NetWasmGcLarge *large_objects;
    NetWasmGcLarge *pending_large;
    NetWasmGcArena *arenas;
    NetWasmGcArena *current_arena;
    size_t next_arena_blocks;
    /* Size classes for objects without references, then with them. */
    NetWasmGcClass classes[2][NWGC_CLASS_COUNT];
    NetWasmGcDirectoryNode *directory_root;
    NetWasmGcDirectoryAllocation *directory_allocations;
    uintptr_t cached_page_key;
    NetWasmGcPage *cached_page;
    size_t object_count;
    size_t live_bytes;
    size_t occupied_bytes;
    size_t backing_bytes;
    size_t marked_object_count;
    size_t marked_live_bytes;
    size_t minimum_collection_budget;
    size_t next_collection_charge;
    uint64_t collections;
    uint64_t allocated_bytes;
    bool collecting;
#ifdef NETWASM_GC_STATISTICS
    NetWasmGcStatistics statistics;
#endif
};

static bool add_overflow(size_t left, size_t right, size_t *result)
{
    if (left > SIZE_MAX - right) return true;
    *result = left + right;
    return false;
}

static bool multiply_overflow(size_t left, size_t right, size_t *result)
{
    if (left != 0 && right > SIZE_MAX / left) return true;
    *result = left * right;
    return false;
}

static bool align_size(size_t value, size_t alignment, size_t *result)
{
    size_t mask = alignment - 1;
    if (value > SIZE_MAX - mask) return false;
    *result = (value + mask) & ~mask;
    return true;
}

static uintptr_t *block_allocated(NetWasmGcBlock *block)
{
    return (uintptr_t *)(block + 1);
}

static const uintptr_t *block_allocated_const(const NetWasmGcBlock *block)
{
    return (const uintptr_t *)(block + 1);
}

static uintptr_t *block_marked(NetWasmGcBlock *block)
{
    return block_allocated(block) + block->bitmap_words;
}

static const uintptr_t *block_marked_const(const NetWasmGcBlock *block)
{
    return block_allocated_const(block) + block->bitmap_words;
}

static uintptr_t *block_pending(NetWasmGcBlock *block)
{
    return block_marked(block) + block->bitmap_words;
}

static unsigned char *block_slots(NetWasmGcBlock *block)
{
    return (unsigned char *)block + block->slots_offset;
}

static const unsigned char *block_slots_const(const NetWasmGcBlock *block)
{
    return (const unsigned char *)block + block->slots_offset;
}

static unsigned char *block_object(NetWasmGcBlock *block, size_t slot)
{
    return block_slots(block) + slot * block->slot_bytes;
}

static unsigned char *large_object(NetWasmGcLarge *large)
{
    return (unsigned char *)large->object;
}

static NetWasmGcClass *block_class(NetWasmGc *gc, const NetWasmGcBlock *block)
{
    return &gc->classes[block->traced][block->class_index];
}

/* A slot always extends past its object, so an object's end is never another
 * object's start and a one-past-end reference names exactly one object. */
static bool slot_size(size_t bytes, size_t *slot_bytes)
{
    return bytes != SIZE_MAX && align_size(bytes + 1, NWGC_ALIGNMENT, slot_bytes);
}

/* An object's size from its layout. */
static size_t object_bytes(const void *object)
{
    NetWasmCollectorLayout layout;
    collector_object_layout(object, &layout);
    return layout.fixed_bytes + layout.element_count * layout.element_size;
}

static bool bit_is_set(const uintptr_t *bits, size_t index)
{
    return (bits[index / NWGC_WORD_BITS] & ((uintptr_t)1 << (index % NWGC_WORD_BITS))) != 0;
}

static void set_bit(uintptr_t *bits, size_t index)
{
    bits[index / NWGC_WORD_BITS] |= (uintptr_t)1 << (index % NWGC_WORD_BITS);
}

static size_t bitmap_population(uintptr_t bits)
{
    return sizeof(bits) == sizeof(unsigned)
        ? (size_t)__builtin_popcount((unsigned)bits)
        : (size_t)__builtin_popcountll((unsigned long long)bits);
}

static size_t bitmap_trailing_zeros(uintptr_t bits)
{
    return sizeof(bits) == sizeof(unsigned)
        ? (size_t)__builtin_ctz((unsigned)bits)
        : (size_t)__builtin_ctzll((unsigned long long)bits);
}

static uintptr_t bitmap_low_mask(size_t bits)
{
    return bits >= NWGC_WORD_BITS ? UINTPTR_MAX : (((uintptr_t)1 << bits) - 1);
}

static uintptr_t bitmap_valid_mask(size_t slot_count, size_t word_index)
{
    size_t first = word_index * NWGC_WORD_BITS;
    size_t remaining = slot_count - first;
    return remaining >= NWGC_WORD_BITS ? UINTPTR_MAX :
        (((uintptr_t)1 << remaining) - 1);
}

static void bitmap_set_range(uintptr_t *bits, size_t first, size_t end)
{
    while (first < end) {
        size_t word = first / NWGC_WORD_BITS;
        size_t word_first = word * NWGC_WORD_BITS;
        size_t word_end = word_first + NWGC_WORD_BITS;
        if (word_end > end) word_end = end;
        size_t low = first - word_first;
        size_t high = word_end - word_first;
        bits[word] |= (UINTPTR_MAX << low) & bitmap_low_mask(high);
        first = word_end;
    }
}

static void bitmap_clear_range(uintptr_t *bits, size_t first, size_t end)
{
    while (first < end) {
        size_t word = first / NWGC_WORD_BITS;
        size_t word_first = word * NWGC_WORD_BITS;
        size_t word_end = word_first + NWGC_WORD_BITS;
        if (word_end > end) word_end = end;
        size_t low = first - word_first;
        size_t high = word_end - word_first;
        bits[word] &= ~((UINTPTR_MAX << low) & bitmap_low_mask(high));
        first = word_end;
    }
}

static void *allocate_directory_storage(NetWasmGc *gc, size_t bytes)
{
    NetWasmGcDirectoryAllocation *allocation = gc->allocate(bytes, NWGC_ALIGNMENT);
    if (allocation == NULL) return NULL;
    memset(allocation, 0, bytes);
    allocation->next = gc->directory_allocations;
    allocation->bytes = bytes;
    gc->directory_allocations = allocation;
    return allocation;
}

static NetWasmGcResult directory_page(
    NetWasmGc *gc,
    uintptr_t page,
    bool create,
    NetWasmGcPage **result)
{
    if (gc->directory_root == NULL) {
        if (!create) {
            *result = NULL;
            return NETWASM_GC_OK;
        }
        gc->directory_root = allocate_directory_storage(gc, sizeof(*gc->directory_root));
        if (gc->directory_root == NULL) return NETWASM_GC_OUT_OF_MEMORY;
    }

    NetWasmGcDirectoryNode *node = gc->directory_root;
    for (size_t level = 0; level + 1 < NWGC_DIRECTORY_LEVELS; level++) {
        size_t shift = (NWGC_DIRECTORY_LEVELS - level - 1) * NWGC_DIRECTORY_BITS;
        size_t index = (size_t)((page >> shift) & NWGC_DIRECTORY_MASK);
        if (level + 2 == NWGC_DIRECTORY_LEVELS) {
            NetWasmGcDirectoryLeaf *leaf = node->entries[index];
            if (leaf == NULL && create) {
                leaf = allocate_directory_storage(gc, sizeof(*leaf));
                if (leaf == NULL) return NETWASM_GC_OUT_OF_MEMORY;
                node->entries[index] = leaf;
            }
            *result = leaf == NULL ? NULL : &leaf->pages[page & NWGC_DIRECTORY_MASK];
            return NETWASM_GC_OK;
        }
        NetWasmGcDirectoryNode *child = node->entries[index];
        if (child == NULL && create) {
            child = allocate_directory_storage(gc, sizeof(*child));
            if (child == NULL) return NETWASM_GC_OUT_OF_MEMORY;
            node->entries[index] = child;
        }
        if (child == NULL) {
            *result = NULL;
            return NETWASM_GC_OK;
        }
        node = child;
    }
    *result = NULL;
    return NETWASM_GC_INVALID_ARGUMENT;
}

static NetWasmGcPage *find_page(const NetWasmGc *gc, uintptr_t page)
{
    NetWasmGcPage *result = NULL;
    (void)directory_page((NetWasmGc *)gc, page, false, &result);
    return result;
}

static NetWasmGcPage *find_page_cached(NetWasmGc *gc, uintptr_t page)
{
    if (gc->cached_page_key == page && gc->cached_page != NULL) return gc->cached_page;
    NetWasmGcPage *entry = find_page(gc, page);
    if (entry != NULL && entry->regions[0] != NULL) {
        gc->cached_page_key = page;
        gc->cached_page = entry;
    }
    return entry;
}

static bool page_has_space(const NetWasmGcPage *entry, const NetWasmGcRegion *region)
{
    return entry == NULL || entry->regions[0] == NULL || entry->regions[0] == region ||
        entry->regions[1] == NULL || entry->regions[1] == region;
}

static NetWasmGcResult register_region(NetWasmGc *gc, NetWasmGcRegion *region)
{
    uintptr_t first = region->start >> NWGC_PAGE_SHIFT;
    uintptr_t last = (region->end - 1) >> NWGC_PAGE_SHIFT;
    for (uintptr_t page = first; page <= last; page++) {
        NetWasmGcPage *entry = NULL;
        NetWasmGcResult result = directory_page(gc, page, true, &entry);
        if (result != NETWASM_GC_OK) return result;
    }
    for (uintptr_t page = first; page <= last; page++)
        if (!page_has_space(find_page(gc, page), region)) return NETWASM_GC_INVALID_ARGUMENT;
    for (uintptr_t page = first; page <= last; page++) {
        NetWasmGcPage *entry = find_page(gc, page);
        if (entry->regions[0] == region || entry->regions[1] == region) continue;
        if (entry->regions[0] == NULL) entry->regions[0] = region;
        else entry->regions[1] = region;
    }
    return NETWASM_GC_OK;
}

static void unregister_region(NetWasmGc *gc, NetWasmGcRegion *region)
{
    uintptr_t first = region->start >> NWGC_PAGE_SHIFT;
    uintptr_t last = (region->end - 1) >> NWGC_PAGE_SHIFT;
    for (uintptr_t page = first; page <= last; page++) {
        NetWasmGcPage *entry = find_page(gc, page);
        if (entry == NULL) continue;
        if (entry->regions[0] == region) {
            entry->regions[0] = entry->regions[1];
            entry->regions[1] = NULL;
        } else if (entry->regions[1] == region) entry->regions[1] = NULL;
        if (entry->regions[0] == NULL) {
            if (gc->cached_page == entry) {
                gc->cached_page_key = 0;
                gc->cached_page = NULL;
            }
        }
    }
}

static size_t block_slot_count(size_t slot_bytes, size_t *words, size_t *slots_offset)
{
    size_t lower = 0;
    size_t upper = (NWGC_BLOCK_BYTES - sizeof(NetWasmGcBlock)) / slot_bytes;
    /* Metadata size grows monotonically with the slot count. Repeatedly
     * recomputing capacity from metadata can oscillate across a word boundary. */
    while (lower < upper) {
        size_t slots = lower + (upper - lower + 1) / 2;
        size_t bitmap_words = (slots + NWGC_WORD_BITS - 1) / NWGC_WORD_BITS;
        size_t bitmap_bytes = bitmap_words * sizeof(uintptr_t) * 3;
        size_t offset;
        if (add_overflow(sizeof(NetWasmGcBlock), bitmap_bytes, &offset) ||
            !align_size(offset, NWGC_ALIGNMENT, &offset)) return 0;
        if (offset <= NWGC_BLOCK_BYTES && slots <= (NWGC_BLOCK_BYTES - offset) / slot_bytes)
            lower = slots;
        else upper = slots - 1;
    }
    *words = (lower + NWGC_WORD_BITS - 1) / NWGC_WORD_BITS;
    if (!align_size(sizeof(NetWasmGcBlock) + *words * sizeof(uintptr_t) * 3,
            NWGC_ALIGNMENT, slots_offset)) return 0;
    return lower;
}

static NetWasmGcResult acquire_block_storage(NetWasmGc *gc, NetWasmGcBlock **result)
{
    NetWasmGcArena *arena = gc->current_arena;
    if (arena == NULL || arena->capacity - arena->used < NWGC_BLOCK_BYTES) {
        size_t blocks = gc->next_arena_blocks == 0 ? 16 : gc->next_arena_blocks;
        size_t payload_bytes;
        size_t allocation_bytes;
        if (multiply_overflow(blocks, NWGC_BLOCK_BYTES, &payload_bytes) ||
            add_overflow(sizeof(NetWasmGcArena), payload_bytes, &allocation_bytes) ||
            gc->backing_bytes > SIZE_MAX - allocation_bytes)
            return NETWASM_GC_SIZE_OVERFLOW;
        arena = gc->allocate(allocation_bytes, NWGC_ALIGNMENT);
        if (arena == NULL) return NETWASM_GC_OUT_OF_MEMORY;
        *arena = (NetWasmGcArena){
            .next = gc->arenas,
            .allocation_bytes = allocation_bytes,
            .capacity = payload_bytes,
        };
        gc->arenas = arena;
        gc->current_arena = arena;
        gc->backing_bytes += allocation_bytes;
        gc->next_arena_blocks = blocks < 256 ? blocks * 2 : 256;
    }
    *result = (NetWasmGcBlock *)(arena->data + arena->used);
    arena->used += NWGC_BLOCK_BYTES;
    return NETWASM_GC_OK;
}

static NetWasmGcResult create_block(
    NetWasmGc *gc, size_t slot_bytes, bool traced, NetWasmGcBlock **result)
{
    size_t words;
    size_t slots_offset;
    size_t slots = block_slot_count(slot_bytes, &words, &slots_offset);
    if (slots == 0) return NETWASM_GC_INVALID_ARGUMENT;
    NetWasmGcBlock *block = gc->empty_blocks;
    if (block != NULL) {
        gc->empty_blocks = block->available_next;
        NWGC_COUNT(gc, recycled_blocks, 1);
    } else {
        NWGC_COUNT(gc, fresh_blocks, 1);
        NetWasmGcResult acquired = acquire_block_storage(gc, &block);
        if (acquired != NETWASM_GC_OK) return acquired;
        block->region = (NetWasmGcRegion){
            .start = (uintptr_t)block,
            .end = (uintptr_t)block + NWGC_BLOCK_BYTES,
            .kind = NWGC_REGION_BLOCK,
        };
        NetWasmGcResult registration = register_region(gc, &block->region);
        if (registration != NETWASM_GC_OK) {
            gc->current_arena->used -= NWGC_BLOCK_BYTES;
            return registration;
        }
    }
    NetWasmGcRegion region = block->region;
    memset(block, 0, slots_offset);
    block->region = region;
    block->next = gc->blocks;
    gc->blocks = block;
    block->slot_bytes = slot_bytes;
    block->slot_count = slots;
    block->bitmap_words = words;
    block->slots_offset = slots_offset;
    block->class_index = slot_bytes / NWGC_ALIGNMENT - 1;
    block->traced = traced;
    *result = block;
    return NETWASM_GC_OK;
}

static size_t find_free_run(NetWasmGc *gc, NetWasmGcBlock *block, size_t maximum, size_t *end)
{
    (void)gc;
    NWGC_COUNT(gc, free_run_searches, 1);
    const uintptr_t *allocated = block_allocated_const(block);
    size_t cursor = block->search;
    size_t first = block->slot_count;
    while (cursor < block->slot_count) {
        size_t word = cursor / NWGC_WORD_BITS;
        size_t bit = cursor % NWGC_WORD_BITS;
        NWGC_COUNT(gc, bitmap_words_searched, 1);
        uintptr_t free = ~allocated[word] & bitmap_valid_mask(block->slot_count, word);
        free &= UINTPTR_MAX << bit;
        if (free != 0) {
            first = word * NWGC_WORD_BITS + bitmap_trailing_zeros(free);
            break;
        }
        cursor = (word + 1) * NWGC_WORD_BITS;
    }
    if (first == block->slot_count) return first;

    size_t limit = first + maximum;
    if (limit < first || limit > block->slot_count) limit = block->slot_count;
    cursor = first;
    while (cursor < limit) {
        size_t word = cursor / NWGC_WORD_BITS;
        size_t bit = cursor % NWGC_WORD_BITS;
        NWGC_COUNT(gc, bitmap_words_searched, 1);
        uintptr_t occupied = allocated[word] & bitmap_valid_mask(block->slot_count, word);
        occupied &= UINTPTR_MAX << bit;
        size_t word_end = (word + 1) * NWGC_WORD_BITS;
        if (word_end > limit) word_end = limit;
        if (occupied != 0) {
            size_t occupied_slot = word * NWGC_WORD_BITS + bitmap_trailing_zeros(occupied);
            if (occupied_slot < word_end) {
                *end = occupied_slot;
                return first;
            }
        }
        cursor = word_end;
    }
    *end = limit;
    return first;
}

static bool is_reserved_tail(const NetWasmGc *gc, const NetWasmGcBlock *block, size_t slot)
{
    const NetWasmGcClass *size_class = &gc->classes[block->traced][block->class_index];
    const unsigned char *address = block_slots_const(block) + slot * block->slot_bytes;
    return size_class->run_block == block &&
        address >= size_class->run_cursor && address < size_class->run_end;
}

static bool collection_due(const NetWasmGc *gc, size_t additional)
{
    return additional >= gc->next_collection_charge ||
        gc->occupied_bytes >= gc->next_collection_charge - additional;
}

static void retire_runs(NetWasmGc *gc)
{
    for (size_t class_index = 0; class_index < 2 * NWGC_CLASS_COUNT; class_index++) {
        NetWasmGcClass *size_class = &gc->classes[class_index / NWGC_CLASS_COUNT][class_index % NWGC_CLASS_COUNT];
        NetWasmGcBlock *block = size_class->run_block;
        if (block == NULL) continue;
        size_t first = (size_t)(size_class->run_cursor - block_slots(block)) / block->slot_bytes;
        size_t end = (size_t)(size_class->run_end - block_slots(block)) / block->slot_bytes;
        bitmap_clear_range(block_allocated(block), first, end);
        block->search = first;
        size_class->run_block = NULL;
        size_class->run_start = size_class->run_cursor = size_class->run_end = NULL;
    }
}

static void rebuild_available(NetWasmGc *gc)
{
    for (size_t index = 0; index < 2 * NWGC_CLASS_COUNT; index++) {
        NetWasmGcClass *size_class = &gc->classes[index / NWGC_CLASS_COUNT][index % NWGC_CLASS_COUNT];
        size_class->available = NULL;
        size_class->run_block = NULL;
        size_class->run_start = size_class->run_cursor = size_class->run_end = NULL;
    }
    for (NetWasmGcBlock *block = gc->blocks; block != NULL; block = block->next) {
        block->available = false;
        block->available_next = NULL;
        if (block->live == 0) continue;
        if (block->live == block->slot_count) continue;
        NetWasmGcClass *size_class = block_class(gc, block);
        block->available_next = size_class->available;
        size_class->available = block;
        block->available = true;
    }
}

/* Counts the objects a run handed out. Live bytes count whole slots: the
 * collector keeps no per-object size. */
static void close_run(NetWasmGc *gc, const NetWasmGcClass *size_class)
{
    size_t consumed = (size_t)(size_class->run_cursor - size_class->run_start);
    gc->object_count += consumed / size_class->run_block->slot_bytes;
    gc->live_bytes += consumed;
}

/* The slots taken from runs still open, which closing them will count. */
static void open_runs(const NetWasmGc *gc, size_t *objects, size_t *bytes)
{
    *objects = 0;
    *bytes = 0;
    for (size_t index = 0; index < 2 * NWGC_CLASS_COUNT; index++) {
        const NetWasmGcClass *size_class = &gc->classes[index / NWGC_CLASS_COUNT][index % NWGC_CLASS_COUNT];
        if (size_class->run_block == NULL) continue;
        size_t consumed = (size_t)(size_class->run_cursor - size_class->run_start);
        *objects += consumed / size_class->run_block->slot_bytes;
        *bytes += consumed;
    }
}

static NetWasmGcResult allocate_small(
    NetWasmGc *gc,
    size_t slot_bytes,
    bool traced,
    void **result)
{
    size_t class_index = slot_bytes / NWGC_ALIGNMENT - 1;
    NetWasmGcClass *size_class = &gc->classes[traced][class_index];
    if (size_class->run_block == NULL || size_class->run_cursor == size_class->run_end) {
        if (size_class->run_block != NULL) {
            close_run(gc, size_class);
            size_class->run_block = NULL;
            size_class->run_start = size_class->run_cursor = size_class->run_end = NULL;
        }
        size_t maximum = NWGC_RUN_BYTES / slot_bytes;
        if (maximum == 0) maximum = 1;
        size_t maximum_run_bytes = maximum * slot_bytes;
        if (collection_due(gc, maximum_run_bytes) && gc->occupied_bytes != 0 && !gc->collecting)
            netwasm_gc_collect(gc);
        NetWasmGcBlock *block = size_class->available;
        size_t first = 0;
        size_t end = 0;
        while (block != NULL) {
            first = find_free_run(gc, block, maximum, &end);
            if (first != block->slot_count) break;
            NWGC_COUNT(gc, exhausted_blocks, 1);
            block->available = false;
            size_class->available = block = block->available_next;
        }
        if (block == NULL) {
            NetWasmGcResult created = create_block(gc, slot_bytes, traced, &block);
            if (created != NETWASM_GC_OK) return created;
            block->available = true;
            block->available_next = size_class->available;
            size_class->available = block;
            first = 0;
            end = maximum < block->slot_count ? maximum : block->slot_count;
        }
        size_t run_bytes = (end - first) * slot_bytes;
        if (gc->occupied_bytes > SIZE_MAX - run_bytes) return NETWASM_GC_SIZE_OVERFLOW;
        bitmap_set_range(block_allocated(block), first, end);
        memset(block_slots(block) + first * slot_bytes, 0, run_bytes);
        NWGC_COUNT(gc, run_refills, 1);
        NWGC_COUNT(gc, run_slots, end - first);
        NWGC_COUNT(gc, run_bytes_zeroed, run_bytes);
        gc->occupied_bytes += run_bytes;
        block->search = end;
        if (end == block->slot_count) {
            size_class->available = block->available_next;
            block->available = false;
        }
        size_class->run_block = block;
        size_class->run_start = size_class->run_cursor = block_slots(block) + first * slot_bytes;
        size_class->run_end = block_slots(block) + end * slot_bytes;
    }
    *result = size_class->run_cursor;
    size_class->run_cursor += slot_bytes;
    return NETWASM_GC_OK;
}

static NetWasmGcResult allocate_large(
    NetWasmGc *gc,
    size_t payload_bytes,
    bool traced,
    void **result)
{
    size_t stored_payload = payload_bytes == 0 ? 1 : payload_bytes;
    size_t allocation_bytes;
    if (add_overflow(sizeof(NetWasmGcLarge), stored_payload, &allocation_bytes))
        return NETWASM_GC_SIZE_OVERFLOW;
    if (collection_due(gc, allocation_bytes) &&
        gc->occupied_bytes != 0 && !gc->collecting) netwasm_gc_collect(gc);
    if (gc->occupied_bytes > SIZE_MAX - allocation_bytes ||
        gc->live_bytes > SIZE_MAX - payload_bytes ||
        gc->backing_bytes > SIZE_MAX - allocation_bytes) return NETWASM_GC_SIZE_OVERFLOW;
    NetWasmGcLarge *large = gc->allocate(allocation_bytes, NWGC_ALIGNMENT);
    if (large == NULL) return NETWASM_GC_OUT_OF_MEMORY;
    memset(large, 0, allocation_bytes);
    large->region = (NetWasmGcRegion){
        .start = (uintptr_t)large,
        .end = (uintptr_t)large + allocation_bytes,
        .kind = NWGC_REGION_LARGE,
    };
    large->allocation_bytes = allocation_bytes;
    large->payload_bytes = payload_bytes;
    large->traced = traced;
    NetWasmGcResult registration = register_region(gc, &large->region);
    if (registration != NETWASM_GC_OK) {
        gc->release(large, allocation_bytes, NWGC_ALIGNMENT);
        return registration;
    }
    large->next = gc->large_objects;
    gc->large_objects = large;
    gc->backing_bytes += allocation_bytes;
    gc->object_count++;
    gc->live_bytes += payload_bytes;
    gc->occupied_bytes += allocation_bytes;
    *result = large_object(large);
    return NETWASM_GC_OK;
}

/* Whether address is inside the object at first, or at its end when endpoint holds. */
static bool object_matches(uintptr_t first, size_t bytes, uintptr_t address, bool endpoint)
{
    uintptr_t last = first + bytes;
    return (address >= first && address < last) || (endpoint && address == last);
}

static bool block_slot_is_live(const NetWasmGc *gc, const NetWasmGcBlock *block, size_t slot)
{
    return slot < block->slot_count && bit_is_set(block_allocated_const(block), slot) &&
        !is_reserved_tail(gc, block, slot);
}

/* Receives a small object as its block and slot, or a large object. */
typedef bool (*ObjectVisitor)(NetWasmGc *, NetWasmGcBlock *, size_t, NetWasmGcLarge *, void *);

static bool visit_block_matches(
    NetWasmGc *gc,
    NetWasmGcBlock *block,
    uintptr_t address,
    bool endpoint,
    ObjectVisitor visitor,
    void *state)
{
    uintptr_t first_slot = (uintptr_t)block_slots(block);
    if (address < first_slot) return false;
    size_t slot = (size_t)((address - first_slot) / block->slot_bytes);
    size_t candidates[2] = {slot, slot == 0 ? SIZE_MAX : slot - 1};
    bool matched = false;
    for (size_t index = 0; index < 2; index++) {
        size_t candidate = candidates[index];
        if (!block_slot_is_live(gc, block, candidate)) continue;
        unsigned char *object = block_object(block, candidate);
        if (!object_matches((uintptr_t)object, object_bytes(object), address, endpoint)) continue;
        matched = visitor(gc, block, candidate, NULL, state) || matched;
    }
    return matched;
}

static bool visit_region_matches(
    NetWasmGc *gc,
    NetWasmGcRegion *region,
    uintptr_t address,
    bool endpoint,
    ObjectVisitor visitor,
    void *state)
{
    if (address < region->start || address > region->end) return false;
    if (region->kind == NWGC_REGION_BLOCK)
        return visit_block_matches(gc, (NetWasmGcBlock *)region, address, endpoint, visitor, state);
    NetWasmGcLarge *large = (NetWasmGcLarge *)region;
    if (!object_matches((uintptr_t)large_object(large), large->payload_bytes, address, endpoint))
        return false;
    return visitor(gc, NULL, 0, large, state);
}

static bool visit_page_matches(
    NetWasmGc *gc,
    NetWasmGcPage *page,
    uintptr_t address,
    bool endpoint,
    ObjectVisitor visitor,
    void *state)
{
    if (page == NULL) return false;
    bool matched = false;
    for (size_t index = 0; index < 2; index++)
        if (page->regions[index] != NULL)
            matched = visit_region_matches(
                gc, page->regions[index], address, endpoint, visitor, state) || matched;
    return matched;
}

static bool visit_address(
    NetWasmGc *gc,
    uintptr_t address,
    bool endpoint,
    ObjectVisitor visitor,
    void *state)
{
    if (address == 0) return false;
    NetWasmGcPage *page = find_page_cached(gc, address >> NWGC_PAGE_SHIFT);
    bool matched = visit_page_matches(gc, page, address, endpoint, visitor, state);
    if (!endpoint || (address & (NWGC_PAGE_BYTES - 1)) != 0) return matched;
    /* An extent's last page is indexed through end - 1. Its logical endpoint
     * may be the first address on the next page, which has no entry for it. */
    NetWasmGcPage *previous = find_page_cached(gc, (address - 1) >> NWGC_PAGE_SHIFT);
    if (previous == NULL) return matched;
    for (size_t index = 0; index < 2; index++) {
        NetWasmGcRegion *region = previous->regions[index];
        if (region == NULL ||
            (page != NULL && (region == page->regions[0] || region == page->regions[1]))) continue;
        matched = visit_region_matches(gc, region, address, true, visitor, state) || matched;
    }
    return matched;
}

static bool mark_resolved(
    NetWasmGc *gc,
    NetWasmGcBlock *block,
    size_t slot,
    NetWasmGcLarge *large)
{
    bool already_marked;
    if (block != NULL) {
        already_marked = bit_is_set(block_marked_const(block), slot);
        if (!already_marked) set_bit(block_marked(block), slot);
    } else {
        already_marked = large->marked;
        large->marked = true;
    }
    if (already_marked) return false;
    NWGC_COUNT(gc, marked_objects, 1);
    gc->marked_object_count++;
    gc->marked_live_bytes += block != NULL ? block->slot_bytes : large->payload_bytes;
    if (block != NULL) {
        if (!block->traced) return true;
        set_bit(block_pending(block), slot);
        if (!block->pending_queued) {
            block->pending_next = gc->pending_blocks;
            gc->pending_blocks = block;
            block->pending_queued = true;
            block->pending_first = slot / NWGC_WORD_BITS;
        } else if (slot / NWGC_WORD_BITS < block->pending_first)
            block->pending_first = slot / NWGC_WORD_BITS;
    } else if (large->traced && !large->pending_queued) {
        large->pending_next = gc->pending_large;
        gc->pending_large = large;
        large->pending_queued = true;
    }
    return true;
}

/* Keep collection policy here so root, interior and exact-reference resolution
 * share the same future generation eligibility and promotion decisions. */
static bool mark_object(NetWasmGc *gc, NetWasmGcBlock *block, size_t slot, NetWasmGcLarge *large, void *state)
{
    (void)state;
    return mark_resolved(gc, block, slot, large);
}

static bool mark_reference(NetWasmGc *gc, uintptr_t address)
{
    if (address == 0) return false;
    NWGC_COUNT(gc, reference_marks, 1);
    if ((address & (NWGC_PAGE_BYTES - 1)) == 0) {
        NWGC_COUNT(gc, reference_fallbacks, 1);
        return visit_address(gc, address, true, mark_object, NULL);
    }
    NetWasmGcPage *page = find_page_cached(gc, address >> NWGC_PAGE_SHIFT);
    if (page == NULL) return false;
    for (size_t index = 0; index < 2; index++) {
        NetWasmGcRegion *region = page->regions[index];
        if (region == NULL) continue;
        if (region->kind == NWGC_REGION_BLOCK) {
            NetWasmGcBlock *block = (NetWasmGcBlock *)region;
            uintptr_t slots = (uintptr_t)block_slots(block);
            if (address < slots) continue;
            size_t distance = (size_t)(address - slots);
            size_t slot = distance / block->slot_bytes;
            /* Collection retires every run first, so an allocated slot is live. */
            if (slot >= block->slot_count || distance != slot * block->slot_bytes ||
                !bit_is_set(block_allocated_const(block), slot)) continue;
            return mark_resolved(gc, block, slot, NULL);
        }
        NetWasmGcLarge *large = (NetWasmGcLarge *)region;
        if ((uintptr_t)large_object(large) == address)
            return mark_resolved(gc, NULL, 0, large);
    }
    /* Packed and unsafe managed fields may contain supported interior or
     * endpoint references. Preserve the general resolver on an exact miss. */
    NWGC_COUNT(gc, reference_fallbacks, 1);
    return visit_page_matches(gc, page, address, true, mark_object, NULL);
}

static bool query_marked(NetWasmGc *gc, NetWasmGcBlock *block, size_t slot, NetWasmGcLarge *large, void *state)
{
    (void)gc;
    bool marked = block != NULL ? bit_is_set(block_marked_const(block), slot) : large->marked;
    *(bool *)state = *(bool *)state || marked;
    return marked;
}

typedef struct {
    uintptr_t address;
    uintptr_t exact;
    uintptr_t interior;
    uintptr_t endpoint;
} BaseQuery;

static bool query_base(NetWasmGc *gc, NetWasmGcBlock *block, size_t slot, NetWasmGcLarge *large, void *state)
{
    (void)gc;
    BaseQuery *query = state;
    unsigned char *object = block != NULL ? block_object(block, slot) : large_object(large);
    uintptr_t first = (uintptr_t)object;
    uintptr_t last = first + (block != NULL ? object_bytes(object) : large->payload_bytes);
    if (query->address == first) query->exact = first;
    else if (query->address < last) query->interior = first;
    else if (query->address == last) query->endpoint = first;
    return true;
}

/* Marks every reference the object's layout names. */
static void trace_object(NetWasmGc *gc, unsigned char *object)
{
    NetWasmCollectorLayout layout;
    collector_object_layout(object, &layout);
    for (size_t field = 0; field < layout.fixed_reference_count; field++) {
        uintptr_t reference;
        memcpy(&reference, object + layout.fixed_offsets[field], sizeof(reference));
        mark_reference(gc, reference);
    }
    unsigned char *item = object + layout.fixed_bytes;
    for (size_t element = 0; element < layout.element_count; element++, item += layout.element_size) {
        for (size_t field = 0; field < layout.element_reference_count; field++) {
            uintptr_t reference;
            memcpy(&reference, item + layout.element_offsets[field], sizeof(reference));
            mark_reference(gc, reference);
        }
    }
}

static void sweep(NetWasmGc *gc)
{
    gc->occupied_bytes = 0;
    NetWasmGcBlock **block_link = &gc->blocks;
    while (*block_link != NULL) {
        NetWasmGcBlock *block = *block_link;
        uintptr_t *allocated = block_allocated(block);
        uintptr_t *marked = block_marked(block);
        uintptr_t *pending = block_pending(block);
        size_t live = 0;
        for (size_t word = 0; word < block->bitmap_words; word++) {
            uintptr_t survivors = marked[word] & bitmap_valid_mask(block->slot_count, word);
            allocated[word] = survivors;
            marked[word] = 0;
            pending[word] = 0;
            live += bitmap_population(survivors);
        }
        block->live = live;
        block->search = 0;
        NWGC_COUNT(gc, swept_blocks, 1);
        gc->occupied_bytes += live * block->slot_bytes;
        if (live == 0) {
            NWGC_COUNT(gc, emptied_blocks, 1);
            *block_link = block->next;
            block->available = false;
            block->available_next = gc->empty_blocks;
            gc->empty_blocks = block;
            continue;
        }
        block_link = &block->next;
    }
    NetWasmGcLarge **link = &gc->large_objects;
    while (*link != NULL) {
        NetWasmGcLarge *large = *link;
        if (!large->marked) {
            NWGC_COUNT(gc, freed_large_objects, 1);
            *link = large->next;
            unregister_region(gc, &large->region);
            gc->backing_bytes -= large->allocation_bytes;
            gc->release(large, large->allocation_bytes, NWGC_ALIGNMENT);
            continue;
        }
        large->marked = false;
        gc->occupied_bytes += large->allocation_bytes;
        link = &large->next;
    }
    gc->live_bytes = gc->marked_live_bytes;
    gc->object_count = gc->marked_object_count;
    rebuild_available(gc);
    gc->next_collection_charge = gc->occupied_bytes >
        (SIZE_MAX - gc->minimum_collection_budget) / 2 ? SIZE_MAX :
        gc->occupied_bytes * 2 + gc->minimum_collection_budget;
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
    gc->next_collection_charge = minimum_collection_budget;
    *result = gc;
    return NETWASM_GC_OK;
}

void netwasm_gc_set_root_tracer(NetWasmGc *gc, NetWasmGcTraceRoots trace_roots, void *state)
{
    if (gc == NULL) return;
    gc->trace_roots = trace_roots;
    gc->root_state = state;
}

void netwasm_gc_set_mark_completion(NetWasmGc *gc, NetWasmGcCompleteMark complete_mark, void *state)
{
    if (gc == NULL) return;
    gc->complete_mark = complete_mark;
    gc->mark_state = state;
}

/* Clang, which builds the runtime archive, inlines this into the allocation
 * path across translation units. GCC, which builds the runtime contracts on
 * Linux, accepts always_inline only on an inline function. */
#if defined(__clang__)
#define NWGC_CROSS_UNIT_INLINE __attribute__((always_inline))
#else
#define NWGC_CROSS_UNIT_INLINE
#endif

/* A bump through the class's open run. Collection retires every run, so this
 * never succeeds while collecting. */
NWGC_CROSS_UNIT_INLINE void *netwasm_gc_try_allocate_available(
    NetWasmGc *gc, size_t bytes, bool contains_references)
{
    if (gc == NULL || bytes >= NWGC_SMALL_LIMIT) return NULL;
    /* The slot of slot_size: at least one byte past the object, aligned. */
    size_t slot_bytes = (bytes + NWGC_ALIGNMENT) & ~(size_t)(NWGC_ALIGNMENT - 1);
    NetWasmGcClass *size_class = &gc->classes[contains_references][slot_bytes / NWGC_ALIGNMENT - 1];
    unsigned char *object = size_class->run_cursor;
    if (object == size_class->run_end) return NULL;
    size_class->run_cursor = object + slot_bytes;
    NWGC_COUNT(gc, fast_allocations, 1);
    gc->allocated_bytes += bytes;
    return object;
}

NetWasmGcResult netwasm_gc_allocate(
    NetWasmGc *gc,
    size_t bytes,
    bool contains_references,
    void **result)
{
    if (gc == NULL || result == NULL || gc->collecting)
        return NETWASM_GC_INVALID_ARGUMENT;
    if (gc->object_count == SIZE_MAX) return NETWASM_GC_SIZE_OVERFLOW;
#ifdef NETWASM_GC_STRESS
    // Qualification only: collect before every allocation.
    if (gc->occupied_bytes != 0) netwasm_gc_collect(gc);
#endif
    size_t slot_bytes;
    if (!slot_size(bytes, &slot_bytes)) return NETWASM_GC_SIZE_OVERFLOW;
    NetWasmGcResult allocation = slot_bytes <= NWGC_SMALL_LIMIT ?
        allocate_small(gc, slot_bytes, contains_references, result) :
        allocate_large(gc, bytes, contains_references, result);
    if (allocation == NETWASM_GC_OK) {
        if (slot_bytes <= NWGC_SMALL_LIMIT) NWGC_COUNT(gc, small_allocations, 1);
        else NWGC_COUNT(gc, large_allocations, 1);
        if (bytes > UINT64_MAX - gc->allocated_bytes) gc->allocated_bytes = UINT64_MAX;
        else gc->allocated_bytes += bytes;
    }
    return allocation;
}

bool netwasm_gc_mark(NetWasmGc *gc, uintptr_t address)
{
    if (gc == NULL || !gc->collecting) return false;
    NWGC_COUNT(gc, root_marks, 1);
    return visit_address(gc, address, true, mark_object, NULL);
}

void netwasm_gc_drain(NetWasmGc *gc)
{
    if (gc == NULL || !gc->collecting) return;
    while (gc->pending_blocks != NULL || gc->pending_large != NULL) {
        if (gc->pending_blocks != NULL) {
            NetWasmGcBlock *block = gc->pending_blocks;
            gc->pending_blocks = block->pending_next;
            while (block->pending_first < block->bitmap_words) {
                size_t word = block->pending_first;
                uintptr_t pending = block_pending(block)[word];
                if (pending == 0) {
                    block->pending_first++;
                    continue;
                }
                size_t bit = bitmap_trailing_zeros(pending);
                block_pending(block)[word] = pending & (pending - 1);
                trace_object(gc, block_object(block, word * NWGC_WORD_BITS + bit));
            }
            block->pending_queued = false;
            block->pending_next = NULL;
            continue;
        }

        NetWasmGcLarge *large = gc->pending_large;
        gc->pending_large = large->pending_next;
        large->pending_next = NULL;
        large->pending_queued = false;
        trace_object(gc, large_object(large));
    }
}

void netwasm_gc_collect(NetWasmGc *gc)
{
    if (gc == NULL || gc->collecting) return;
    gc->collecting = true;
    retire_runs(gc);
    gc->pending_blocks = NULL;
    gc->pending_large = NULL;
    gc->marked_object_count = 0;
    gc->marked_live_bytes = 0;
    if (gc->trace_roots != NULL) gc->trace_roots(gc, gc->root_state);
    netwasm_gc_drain(gc);
    if (gc->complete_mark != NULL) gc->complete_mark(gc, gc->mark_state);
    netwasm_gc_drain(gc);
    sweep(gc);
    gc->collections++;
    gc->collecting = false;
}

bool netwasm_gc_is_marked(const NetWasmGc *gc, uintptr_t address)
{
    if (gc == NULL || address == 0) return false;
    bool marked = false;
    visit_address((NetWasmGc *)gc, address, true, query_marked, &marked);
    return marked;
}

uintptr_t netwasm_gc_base(const NetWasmGc *gc, uintptr_t address)
{
    if (gc == NULL || address == 0) return 0;
    BaseQuery query = {.address = address};
    visit_address((NetWasmGc *)gc, address, true, query_base, &query);
    return query.exact != 0 ? query.exact : query.interior != 0 ? query.interior : query.endpoint;
}

bool netwasm_gc_contains(const NetWasmGc *gc, uintptr_t address)
{
    return netwasm_gc_base(gc, address) != 0;
}

bool netwasm_gc_is_collecting(const NetWasmGc *gc)
{
    return gc != NULL && gc->collecting;
}

uint64_t netwasm_gc_collection_count(const NetWasmGc *gc)
{
    return gc == NULL ? 0 : gc->collections;
}

uint64_t netwasm_gc_total_allocated_bytes(const NetWasmGc *gc)
{
    return gc == NULL ? 0 : gc->allocated_bytes;
}

uint64_t netwasm_gc_live_bytes(const NetWasmGc *gc)
{
    if (gc == NULL) return 0;
    size_t objects, bytes;
    open_runs(gc, &objects, &bytes);
    return gc->live_bytes + bytes;
}

uint64_t netwasm_gc_reserved_bytes(const NetWasmGc *gc)
{
    return gc == NULL ? 0 : gc->backing_bytes;
}

size_t netwasm_gc_object_count(const NetWasmGc *gc)
{
    if (gc == NULL) return 0;
    size_t objects, bytes;
    open_runs(gc, &objects, &bytes);
    return gc->object_count + objects;
}

#ifdef NETWASM_GC_STATISTICS
void netwasm_gc_read_statistics(const NetWasmGc *gc, NetWasmGcStatistics *result)
{
    *result = gc->statistics;
    result->collections = gc->collections;
    result->object_count = netwasm_gc_object_count(gc);
    for (size_t index = 0; index < 2 * NWGC_CLASS_COUNT; index++) {
        const NetWasmGcClass *size_class = &gc->classes[index / NWGC_CLASS_COUNT][index % NWGC_CLASS_COUNT];
        result->reserved_run_bytes += (uint64_t)(size_class->run_end - size_class->run_cursor);
    }
    uint64_t large_payload = 0;
    for (const NetWasmGcLarge *large = gc->large_objects; large != NULL; large = large->next) {
        result->large_object_count++;
        result->large_bytes += large->allocation_bytes;
        large_payload += large->payload_bytes;
    }
    uint64_t small_payload = 0;
    for (const NetWasmGcBlock *block = gc->blocks; block != NULL; block = block->next) {
        const uintptr_t *allocated = block_allocated_const(block);
        size_t assigned = 0;
        for (size_t word = 0; word < block->bitmap_words; word++)
            assigned += bitmap_population(allocated[word] & bitmap_valid_mask(block->slot_count, word));
        size_t live = 0;
        for (size_t slot = 0; slot < block->slot_count; slot++) {
            if (!block_slot_is_live(gc, block, slot)) continue;
            live++;
            small_payload += object_bytes(block_slots_const(block) + slot * block->slot_bytes);
        }
        result->block_count++;
        result->block_metadata_bytes += block->slots_offset;
        result->block_tail_bytes += NWGC_BLOCK_BYTES - block->slots_offset - block->slot_count * block->slot_bytes;
        result->small_occupied_bytes += live * block->slot_bytes;
        result->free_slot_bytes += (block->slot_count - assigned) * block->slot_bytes;
    }
    /* Objects carry no collector header; live bytes count whole slots, and
     * the layout tells padding from payload. */
    result->small_header_bytes = 0;
    result->live_payload_bytes = small_payload + large_payload;
    result->small_padding_bytes = result->small_occupied_bytes - small_payload;
    for (const NetWasmGcBlock *block = gc->empty_blocks; block != NULL; block = block->available_next)
        result->empty_block_count++;
    for (const NetWasmGcArena *arena = gc->arenas; arena != NULL; arena = arena->next) {
        result->arena_bytes += arena->capacity;
        result->arena_header_bytes += arena->allocation_bytes - arena->capacity;
        result->unused_arena_bytes += arena->capacity - arena->used;
    }
    for (const NetWasmGcDirectoryAllocation *directory = gc->directory_allocations;
            directory != NULL; directory = directory->next)
        result->directory_bytes += directory->bytes;
}
#endif

void netwasm_gc_destroy(NetWasmGc *gc)
{
    if (gc == NULL) return;
    NetWasmGcLarge *large = gc->large_objects;
    while (large != NULL) {
        NetWasmGcLarge *next = large->next;
        gc->release(large, large->allocation_bytes, NWGC_ALIGNMENT);
        large = next;
    }
    NetWasmGcArena *arena = gc->arenas;
    while (arena != NULL) {
        NetWasmGcArena *next = arena->next;
        gc->release(arena, arena->allocation_bytes, NWGC_ALIGNMENT);
        arena = next;
    }
    NetWasmGcDirectoryAllocation *directory = gc->directory_allocations;
    while (directory != NULL) {
        NetWasmGcDirectoryAllocation *next = directory->next;
        gc->release(directory, directory->bytes, NWGC_ALIGNMENT);
        directory = next;
    }
    NetWasmGcRelease release = gc->release;
    release(gc, sizeof(*gc), alignof(NetWasmGc));
}
