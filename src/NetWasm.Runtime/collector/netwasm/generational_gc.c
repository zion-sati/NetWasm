/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */

/* NetWasmGC with generations: the collector of netwasm_gc.c, which this file includes, with what follows at its
 * generation points (generation_points.h).
 *
 * Generations are kept by the mark bits. A collection leaves its survivors marked, so they are old, and what is
 * allocated after it is young. A minor collection marks from the roots and from the old objects that a store of
 * a reference has written into since the last collection, which their cards record (reference_cards.h), and its
 * sweep frees the young objects it did not reach. A collection of every object clears the marks first, and is
 * due when the pacing rule of netwasm_gc.c says, so the heap is bounded exactly as it is without generations.
 *
 * A minor collection is due each time the heap has grown by the minimum budget. One that would keep most of what
 * it looks at costs as much as a collection of every object and frees little, so it is given up, and minor
 * collections then wait: see collect_minor and give_up. */
#include "generational_gc.h"
#include "reference_cards.h"

#define NWGC_GENERATION_POINTS
#define generations_keep_marks true
static NetWasmGcResult generations_begin(NetWasmGc *gc);
static void generations_end(NetWasmGc *gc);
static NetWasmGcResult generations_cover(NetWasmGc *gc, uintptr_t start, uintptr_t end);
static unsigned char *leaf_block(NetWasmGc *gc, unsigned char **end);
static void generations_collect_due(NetWasmGc *gc);
static void generations_collect_young(NetWasmGc *gc);
static void generations_collect_all(NetWasmGc *gc);
static bool generations_may_mark(const NetWasmGc *gc);
static void generations_forget_writes(bool traced, uintptr_t start, uintptr_t end);
struct NetWasmGcBlock;
struct NetWasmGcLarge;
static void generations_note_allocation(struct NetWasmGcBlock *block);
static void generations_note_large(struct NetWasmGcLarge *large);
static bool generations_block_unchanged(const NetWasmGc *gc, const struct NetWasmGcBlock *block);
static bool generations_large_unchanged(const NetWasmGc *gc, const struct NetWasmGcLarge *large);
static void generations_note_swept(struct NetWasmGcBlock *block);
static void generations_note_swept_large(struct NetWasmGcLarge *large);
static size_t generations_pace(NetWasmGc *gc, size_t charge);
static bool generations_minor_due(const NetWasmGc *gc);
static uint64_t generations_minor_collections(const NetWasmGc *gc);
static unsigned generations_of(const NetWasmGc *gc, uintptr_t address);

#include "netwasm_gc.c"

uintptr_t netwasm_gc_card_directory[NWGC_CARD_DIRECTORY_ENTRIES];
unsigned char netwasm_gc_card_sink[NWGC_CARD_LEAF_CARDS];

/* Storage that leaves are cut from, with what gives it back. */
typedef struct NetWasmGcLeafSlab NetWasmGcLeafSlab;
struct NetWasmGcLeafSlab {
    NetWasmGcLeafSlab *next;
    size_t bytes;
};

/* The directory is the process's, and a collection clears the cards it has read, so one collector with
 * generations exists at a time and its generations are kept here. */
static struct {
    NetWasmGc *collector;
    /* Where the next leaf is cut from in the newest slab, and that slab's end. */
    unsigned char *leaf_storage;
    unsigned char *leaf_storage_end;
    /* The leaf slabs, newest first, released when generations end. */
    NetWasmGcLeafSlab *leaf_slabs;
    /* When the next collection of every object is due. */
    size_t next_full_charge;
    /* The next collection is of every object, and the one under way is. */
    bool full_next;
    bool full;
    /* A minor collection is given up once it has marked more than mark_limit bytes. Minor collections are then
     * off until the first collection of every object after the program has allocated backoff bytes more: four
     * minimum budgets, growing fourfold each time that happens, up to sixty-four; a minor collection that runs
     * to its end starts it over. The wait is in bytes, not in collections, because a big heap's collections are
     * far apart and are where minor ones pay most. Every collection sets the limit before it marks, and one that
     * runs to its end lifts it to SIZE_MAX; a given-up minor collection leaves the limit it passed, for it left
     * some young objects marked that no minor collection may take for old: until a collection of every object
     * clears the marks, every collection is one. */
    size_t mark_limit;
    /* Bytes of old objects a minor collection traces because a store set their cards. */
    size_t work;
    bool minors_off;
    uint64_t minors_resume_at;
    uint64_t backoff;
    /* What the last collection kept: the old objects. */
    size_t old_object_count;
    size_t old_live_bytes;
    uint64_t minor_collections;
    uint64_t given_up;
} generations;

static NetWasmGcResult generations_begin(NetWasmGc *gc)
{
    if (generations.collector != NULL) return NETWASM_GC_INVALID_ARGUMENT;
    memset(&generations, 0, sizeof(generations));
    generations.collector = gc;
    generations.next_full_charge = gc->minimum_collection_budget;
    generations.mark_limit = SIZE_MAX;
    generations.leaf_storage = leaf_block(gc, &generations.leaf_storage_end);
    if (generations.leaf_storage == NULL) {
        generations.collector = NULL;
        return NETWASM_GC_OUT_OF_MEMORY;
    }
    return NETWASM_GC_OK;
}

/* Releases the slabs that hold every leaf and clears the directory. */
static void generations_end(NetWasmGc *gc)
{
    NetWasmGcLeafSlab *slab = generations.leaf_slabs;
    while (slab != NULL) {
        NetWasmGcLeafSlab *next = slab->next;
        gc->release(slab, slab->bytes, NWGC_ALIGNMENT);
        slab = next;
    }
    memset(netwasm_gc_card_directory, 0, sizeof(netwasm_gc_card_directory));
    generations.leaf_slabs = NULL;
    generations.collector = NULL;
}

/* Storage for leaves, in slabs of their own. The first is taken when the collector starts, at the bottom of the
 * heap, and holds the leaves of the first 31 MiB the heap reaches on wasm32 (7 spans of 4 MiB on wasm64); each
 * later one, taken where the heap ends when the last is full, doubles, up to a megabyte. A leaf never takes an
 * arena's block, which would bring the next arena forward. */
static unsigned char *leaf_block(NetWasmGc *gc, unsigned char **end)
{
    size_t bytes = generations.leaf_slabs == NULL ? 2 * NWGC_BLOCK_BYTES : generations.leaf_slabs->bytes * 2;
    if (bytes > 16 * NWGC_BLOCK_BYTES) bytes = 16 * NWGC_BLOCK_BYTES;
    NetWasmGcLeafSlab *slab = gc->allocate(bytes, NWGC_ALIGNMENT);
    if (slab == NULL) return NULL;
    slab->next = generations.leaf_slabs;
    slab->bytes = bytes;
    generations.leaf_slabs = slab;
    *end = (unsigned char *)slab + bytes;
    return (unsigned char *)(slab + 1);
}

/* A leaf's cards, cut from the leaf slabs, each on a cache line. A leaf lives as long as the collector. Taken from
 * the allocator one by one, it would sit at the end of the heap, after the large object whose memory reached a new
 * span, and keep that object's memory from coalescing with the next one's once both are dead: a program that keeps
 * a few large arrays alive at a time would grow its heap. */
static unsigned char *leaf_cards(NetWasmGc *gc)
{
    uintptr_t next = ((uintptr_t)generations.leaf_storage + NWGC_CARD_LINE_BYTES - 1) &
        ~(uintptr_t)(NWGC_CARD_LINE_BYTES - 1);
    if (generations.leaf_storage == NULL || next > (uintptr_t)generations.leaf_storage_end ||
            (uintptr_t)generations.leaf_storage_end - next < NWGC_CARD_LEAF_CARDS) {
        unsigned char *storage = leaf_block(gc, &generations.leaf_storage_end);
        if (storage == NULL) return NULL;
        next = ((uintptr_t)storage + NWGC_CARD_LINE_BYTES - 1) & ~(uintptr_t)(NWGC_CARD_LINE_BYTES - 1);
    }
    generations.leaf_storage = (unsigned char *)(next + NWGC_CARD_LEAF_CARDS);
    return (unsigned char *)next;
}

/* Gives every span the memory reaches a leaf, so that a store into it is recorded. */
static NetWasmGcResult generations_cover(NetWasmGc *gc, uintptr_t start, uintptr_t end)
{
    uintptr_t last = (end - 1) >> NWGC_CARD_SPAN_SHIFT;
    for (uintptr_t span = start >> NWGC_CARD_SPAN_SHIFT; span <= last; span++) {
        size_t entry = (size_t)span & (NWGC_CARD_DIRECTORY_ENTRIES - 1);
        if (netwasm_gc_card_directory[entry] != 0) continue;
        unsigned char *cards = leaf_cards(gc);
        if (cards == NULL) return NETWASM_GC_OUT_OF_MEMORY;
        memset(cards, 0, NWGC_CARD_LEAF_CARDS);
        netwasm_gc_card_directory[entry] = (uintptr_t)cards - (uintptr_t)netwasm_gc_card_sink;
    }
    return NETWASM_GC_OK;
}

/* The first card at or after the address, and below end, that a store set; end when there is none. Clear
 * cards are read eight at a time. */
static uintptr_t next_set_card(uintptr_t address, uintptr_t end)
{
    uintptr_t card = address & ~(NWGC_CARD_BYTES - 1);
    while (card < end) {
        const unsigned char *byte = netwasm_gc_card(card);
        if ((((size_t)(card >> NWGC_CARD_SHIFT)) & 7) == 0 && end - card >= 8 * NWGC_CARD_BYTES) {
            uint64_t eight;
            memcpy(&eight, byte, sizeof(eight));
            if (eight == 0) {
                card += 8 * NWGC_CARD_BYTES;
                continue;
            }
        }
        if (*byte != 0) return card;
        if (end - card <= NWGC_CARD_BYTES) break;
        card += NWGC_CARD_BYTES;
    }
    return end;
}

/* The sweep has gone over [start, end): every survivor is old now and nothing is young, so no store made into
 * them so far matters any more, and every card the range overlaps is cleared. */
static void generations_forget_writes(bool traced, uintptr_t start, uintptr_t end)
{
    if (!traced || end <= start) return;
    uintptr_t card = start & ~(NWGC_CARD_BYTES - 1);
    for (;;) {
        uintptr_t span_last = card | (NWGC_CARD_SPAN_BYTES - 1);
        uintptr_t last = end - 1 < span_last ? end - 1 : span_last;
        memset(netwasm_gc_card(card), 0, (size_t)((last - card) >> NWGC_CARD_SHIFT) + 1);
        if (last == end - 1) return;
        card = span_last + 1;
    }
}

/* Queues an old object of a traced block for tracing, as marking queues one it has just marked. */
static void queue_old_object(NetWasmGc *gc, NetWasmGcBlock *block, size_t slot)
{
    set_bit(block_pending(block), slot);
    if (!block->pending_queued) {
        block->pending_next = gc->pending_blocks;
        gc->pending_blocks = block;
        block->pending_queued = true;
        block->pending_first = slot / NWGC_WORD_BITS;
    } else if (slot / NWGC_WORD_BITS < block->pending_first)
        block->pending_first = slot / NWGC_WORD_BITS;
}

static void mark_references_at(NetWasmGc *gc, const unsigned char *base, const size_t *offsets, size_t count)
{
    for (size_t field = 0; field < count; field++) {
        uintptr_t reference;
        memcpy(&reference, base + offsets[field], sizeof(reference));
        mark_reference(gc, reference);
    }
}

/* A minor collection's other roots: what an old object may hold of the young ones. Only a store made since the
 * last collection can have put a young object's reference into an old one, and each such store set the card of
 * the address it wrote through, which lies inside what it wrote into.
 *
 * A small old object that overlaps a set card is queued and traced whole. A large one is traced only where its
 * cards are set: its fixed part when a card overlaps that, and the elements that overlap a set card, so a store
 * into one element of a long array costs that element's card and not the array.
 *
 * The bytes traced are the collection's work so far; it stops, and answers false, once they pass the limit. */
static bool trace_written_old_objects(NetWasmGc *gc, size_t limit)
{
    for (NetWasmGcBlock *block = gc->blocks; block != NULL; block = block->next) {
        if (!block->traced) continue;
        uintptr_t slots = (uintptr_t)block_slots(block);
        uintptr_t end = slots + block->slot_count * block->slot_bytes;
        uintptr_t card = next_set_card(slots, end);
        if (card < end) block->written = true;
        while (card < end) {
            bool final = end - card <= NWGC_CARD_BYTES;
            /* Divides, not block_slot_at: the multiply measured slower here (trees 4%), though faster in the
             * marker. */
            size_t slot = card <= slots ? 0 : (size_t)((card - slots) / block->slot_bytes);
            size_t last = final ? block->slot_count :
                (size_t)((card + NWGC_CARD_BYTES - slots + block->slot_bytes - 1) / block->slot_bytes);
            for (; slot < last; slot++) {
                if (!bit_is_set(block_marked_const(block), slot) || bit_is_set(block_pending(block), slot))
                    continue;
                generations.work += block->slot_bytes;
                queue_old_object(gc, block, slot);
            }
            if (generations.work > limit) return false;
            card = final ? end : next_set_card(card + NWGC_CARD_BYTES, end);
        }
    }
    for (NetWasmGcLarge *large = gc->large_objects; large != NULL; large = large->next) {
        if (!large->traced || !large->marked) continue;
        const unsigned char *object = large_object(large);
        uintptr_t first = (uintptr_t)object;
        uintptr_t end = first + large->payload_bytes;
        uintptr_t card = next_set_card(first, end);
        if (card >= end) continue;
        large->written = true;
        NetWasmCollectorLayout layout;
        collector_object_layout(object, &layout);
        uintptr_t elements = first + layout.fixed_bytes;
        bool fixed_traced = false;
        size_t traced_elements = 0;
        while (card < end) {
            bool final = end - card <= NWGC_CARD_BYTES;
            if (card < elements && !fixed_traced) {
                fixed_traced = true;
                generations.work += layout.fixed_bytes;
                mark_references_at(gc, object, layout.fixed_offsets, layout.fixed_reference_count);
            }
            if ((final || card + NWGC_CARD_BYTES > elements) &&
                layout.element_reference_count != 0 && layout.element_size != 0) {
                size_t element = card <= elements ? 0 : (size_t)((card - elements) / layout.element_size);
                size_t stop = final ? layout.element_count :
                    (size_t)((card + NWGC_CARD_BYTES - elements + layout.element_size - 1) / layout.element_size);
                if (stop > layout.element_count) stop = layout.element_count;
                if (element < traced_elements) element = traced_elements;
                if (element < stop) {
                    generations.work += (stop - element) * layout.element_size;
                    traced_elements = stop;
                }
                for (; element < stop; element++)
                    mark_references_at(gc, (const unsigned char *)elements + element * layout.element_size,
                        layout.element_offsets, layout.element_reference_count);
            }
            if (generations.work > limit) return false;
            card = final ? end : next_set_card(card + NWGC_CARD_BYTES, end);
        }
    }
    return true;
}

/* The mark limit is looked at once for each block of pending objects, not once for each object. */
static bool generations_may_mark(const NetWasmGc *gc)
{
    return gc->marked_live_bytes <= generations.mark_limit;
}

/* What a minor collection's sweep may leave as it stands. A block holds young objects only after a run was opened in
 * it, and a store set a card over it only if trace_written_old_objects found the card set: a block with neither
 * holds old objects alone, all marked and kept, and its cards are clear, so its sweep would change nothing. An old
 * large object whose cards are clear needs them cleared no more. A collection of every object sweeps everything. */
static void generations_note_allocation(NetWasmGcBlock *block)
{
    block->touched = true;
}

static void generations_note_large(NetWasmGcLarge *large)
{
    large->young = true;
}

static bool generations_block_unchanged(const NetWasmGc *gc, const NetWasmGcBlock *block)
{
    (void)gc;
    return !generations.full && !block->touched && !block->written;
}

static bool generations_large_unchanged(const NetWasmGc *gc, const NetWasmGcLarge *large)
{
    (void)gc;
    return !generations.full && !large->young && !large->written;
}

static void generations_note_swept(NetWasmGcBlock *block)
{
    block->touched = false;
    block->written = false;
}

static void generations_note_swept_large(NetWasmGcLarge *large)
{
    large->young = false;
    large->written = false;
}

/* A collection of every object forgets which objects are old. */
static void generations_collect_all(NetWasmGc *gc)
{
    generations.full = true;
    generations.mark_limit = SIZE_MAX;
    for (NetWasmGcBlock *block = gc->blocks; block != NULL; block = block->next)
        memset(block_marked(block), 0, block->bitmap_words * sizeof(uintptr_t));
    for (NetWasmGcLarge *large = gc->large_objects; large != NULL; large = large->next) large->marked = false;
}

/* After a sweep: what it kept is old, and the next collection is chosen. A collection of every object sets when
 * the next one is due, by the pacing rule's charge. Until then a minor collection is due each time the heap has
 * grown by the minimum budget, while minor collections pay. */
static size_t generations_pace(NetWasmGc *gc, size_t charge)
{
    generations.old_live_bytes = gc->live_bytes;
    generations.old_object_count = gc->object_count;
    if (generations.full) {
        generations.next_full_charge = charge;
        generations.minors_off = gc->allocated_bytes < generations.minors_resume_at;
    } else generations.backoff = 0;
    size_t minor_charge;
    generations.full_next = generations.minors_off ||
        add_overflow(gc->occupied_bytes, gc->minimum_collection_budget, &minor_charge) ||
        minor_charge >= generations.next_full_charge;
    return generations.full_next ? generations.next_full_charge : minor_charge;
}

/* Leaves a minor collection that passed its mark limit before anything but marking was done. Nothing is swept,
 * so nothing is lost; the marks it set stay until the next collection, which is of every object and clears
 * them, and the cards stay set for the same reason.
 *
 * The collection closed every run, which a sweep would have accounted for. So the heap is counted again as it
 * stands, a run's unused slots are free again, and the blocks with room are found again: the pacing rule reads
 * what the heap occupies, and allocation goes on from here. */
static void give_up(NetWasmGc *gc)
{
    for (NetWasmGcBlock *block = gc->blocks; block != NULL; block = block->next) {
        if (!block->traced) continue;
        memset(block_pending(block), 0, block->bitmap_words * sizeof(uintptr_t));
        block->pending_queued = false;
        block->pending_next = NULL;
    }
    for (NetWasmGcLarge *large = gc->large_objects; large != NULL; large = large->next) {
        large->pending_queued = false;
        large->pending_next = NULL;
    }
    gc->pending_blocks = NULL;
    gc->pending_large = NULL;

    size_t small_before = gc->occupied_bytes - gc->large_occupied_bytes;
    if (small_before > gc->small_occupied_peak) gc->small_occupied_peak = small_before;
    size_t objects = 0;
    size_t small_bytes = 0;
    for (NetWasmGcBlock *block = gc->blocks; block != NULL; block = block->next) {
        const uintptr_t *allocated = block_allocated_const(block);
        size_t live = 0;
        for (size_t word = 0; word < block->bitmap_words; word++)
            live += bitmap_population(allocated[word] & bitmap_valid_mask(block->slot_count, word));
        block->live = live;
        block->search = 0;
        objects += live;
        small_bytes += live * block->slot_bytes;
    }
    size_t large_payload = 0;
    for (NetWasmGcLarge *large = gc->large_objects; large != NULL; large = large->next) {
        objects++;
        large_payload += large->payload_bytes;
    }
    gc->object_count = objects;
    gc->live_bytes = small_bytes + large_payload;
    gc->occupied_bytes = small_bytes + gc->large_occupied_bytes;
    rebuild_available(gc);

    uint64_t budget = gc->minimum_collection_budget;
    generations.backoff = generations.backoff == 0 ? 4 * budget
        : generations.backoff >= 64 * budget ? 64 * budget : generations.backoff * 4;
    generations.minors_resume_at = gc->allocated_bytes > UINT64_MAX - generations.backoff
        ? UINT64_MAX : gc->allocated_bytes + generations.backoff;
    generations.minors_off = true;
    generations.full_next = true;
    gc->next_collection_charge = generations.next_full_charge;
    generations.given_up++;
}

#ifdef NETWASM_GC_STRESS
static bool find_unmarked(NetWasmGc *gc, NetWasmGcBlock *block, size_t slot, NetWasmGcLarge *large, void *state)
{
    (void)gc;
    bool marked = block != NULL ? bit_is_set(block_marked_const(block), slot) : large->marked;
    if (!marked) *(bool *)state = true;
    return true;
}

static void check_references_at(
    NetWasmGc *gc, const unsigned char *holder, const unsigned char *base, const size_t *offsets, size_t count)
{
    for (size_t field = 0; field < count; field++) {
        uintptr_t reference;
        memcpy(&reference, base + offsets[field], sizeof(reference));
        bool unmarked = false;
        visit_address(gc, reference, true, find_unmarked, &unmarked);
        if (unmarked) collector_missed_reference_write(holder, base + offsets[field], reference);
    }
}

static void check_object_references(NetWasmGc *gc, const unsigned char *object)
{
    NetWasmCollectorLayout layout;
    collector_object_layout(object, &layout);
    check_references_at(gc, object, object, layout.fixed_offsets, layout.fixed_reference_count);
    const unsigned char *item = object + layout.fixed_bytes;
    for (size_t element = 0; element < layout.element_count; element++, item += layout.element_size)
        check_references_at(gc, object, item, layout.element_offsets, layout.element_reference_count);
}

/* The write barrier's check, for qualification: once a minor collection has marked, every reference a marked
 * object holds names a marked object. A young object's do because it was traced, and an old one's do unless a
 * store put a young object's reference into it without setting a card, which is a store the compiler or the
 * runtime left without its barrier. The sweep would free that young object under its holder. */
static void check_reference_writes(NetWasmGc *gc)
{
    for (NetWasmGcBlock *block = gc->blocks; block != NULL; block = block->next) {
        if (!block->traced) continue;
        for (size_t slot = 0; slot < block->slot_count; slot++)
            if (bit_is_set(block_marked_const(block), slot))
                check_object_references(gc, block_object(block, slot));
    }
    for (NetWasmGcLarge *large = gc->large_objects; large != NULL; large = large->next)
        if (large->traced && large->marked) check_object_references(gc, large_object(large));
}
#endif

/* The start of a minor collection: the old objects count as marked. */
static inline __attribute__((always_inline)) void begin_minor(NetWasmGc *gc)
{
    gc->collecting = true;
    generations.full = false;
    retire_runs(gc);
    gc->pending_blocks = NULL;
    gc->pending_large = NULL;
    generations.mark_limit = SIZE_MAX;
    gc->marked_object_count = generations.old_object_count;
    gc->marked_live_bytes = generations.old_live_bytes;
    generations.work = 0;
}

/* The rest of a minor collection, once the written old objects are traced: the roots, then the collection is given
 * up past the mark limit or runs to its end. A given-up one leaves the limit it passed (see mark_limit). */
static inline __attribute__((always_inline)) void finish_minor(NetWasmGc *gc)
{
    if (generations_may_mark(gc) && gc->trace_roots != NULL) gc->trace_roots(gc, gc->root_state);
    netwasm_gc_drain(gc);
    if (!generations_may_mark(gc)) {
        give_up(gc);
        gc->collecting = false;
        return;
    }
    /* From here the collection runs to its end: completing the mark clears weak references and queues
     * finalizers by what is marked, which a mark cut short would get wrong. */
    generations.mark_limit = SIZE_MAX;
    if (gc->complete_mark != NULL) gc->complete_mark(gc, gc->mark_state);
    netwasm_gc_drain(gc);
#ifdef NETWASM_GC_STRESS
    check_reference_writes(gc);
#endif
    generations.minor_collections++;
    sweep(gc);
    gc->collections++;
    gc->collecting = false;
}

/* A minor collection pays while its work stays under what collections of every object spend to free the same
 * bytes: with L bytes alive and a minimum budget B, such a collection marks L to free L + B, so a minor one
 * over Y young bytes may spend Y * L / (L + B). Its work is the old objects it traces and three times the young
 * bytes it marks: those stay, and the next collection of every object marks them again. */
static void collect_minor(NetWasmGc *gc)
{
    begin_minor(gc);
#ifdef NETWASM_GC_STRESS
    /* Qualification runs every minor collection to its end, so that each one is checked. */
    trace_written_old_objects(gc, SIZE_MAX);
#else
    size_t young = gc->occupied_bytes > gc->survived_bytes ? gc->occupied_bytes - gc->survived_bytes : 0;
    uint64_t live = gc->survived_bytes;
    size_t limit = (size_t)((uint64_t)young * live / (live + gc->minimum_collection_budget));
    generations.mark_limit = trace_written_old_objects(gc, limit)
        ? generations.old_live_bytes + (limit - generations.work) / 3 : 0;
#endif
    finish_minor(gc);
}

/* The collection an allocation or an idle embedder asks for: a minor one, unless nothing is old yet, minor
 * collections are off, or the heap has reached what the pacing rule allows since the last collection of every
 * object. */
static void generations_collect_due(NetWasmGc *gc)
{
#ifdef NETWASM_GC_STRESS
    /* Qualification collects before every allocation: minor collections, each checked, and every eighth a
     * collection of every object. */
    bool full = gc->survived_bytes == 0 || gc->collections % 8 == 7;
#else
    bool full = gc->survived_bytes == 0 || generations.full_next ||
        gc->occupied_bytes >= generations.next_full_charge;
#endif
    if (full) netwasm_gc_collect(gc);
    else collect_minor(gc);
}

/* GC.Collect(0): a minor collection, run to its end whatever it marks, as the program asked for one. After a minor
 * collection was given up, the collection of every object that must come next. */
static void generations_collect_young(NetWasmGc *gc)
{
    if (generations.mark_limit != SIZE_MAX) {
        netwasm_gc_collect(gc);
        return;
    }
    begin_minor(gc);
    trace_written_old_objects(gc, SIZE_MAX);
    finish_minor(gc);
}

/* As generations_collect_due chooses outside qualification. */
static bool generations_minor_due(const NetWasmGc *gc)
{
    return gc == generations.collector && gc->survived_bytes != 0 && !generations.full_next &&
        gc->occupied_bytes < generations.next_full_charge;
}

static uint64_t generations_minor_collections(const NetWasmGc *gc)
{
    return gc == generations.collector ? generations.minor_collections : 0;
}

/* An object a collection has kept is marked until the next collection of every object begins. Between a minor
 * collection that was given up and the next collection, an object that one reached reports the oldest
 * generation too: .NET's GC.GetGeneration is advice, and nothing of the collector's depends on it. */
static unsigned generations_of(const NetWasmGc *gc, uintptr_t address)
{
    return netwasm_gc_is_marked(gc, address) ? 1u : 0u;
}

uint64_t netwasm_gc_minor_collection_count(const NetWasmGc *gc)
{
    return gc == NULL ? 0 : generations_minor_collections(gc);
}

uint64_t netwasm_gc_given_up_minor_collection_count(const NetWasmGc *gc)
{
    return gc == NULL || gc != generations.collector ? 0 : generations.given_up;
}
