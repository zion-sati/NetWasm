// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

/* NetWasmGC with generations (generational_gc.c): what survives a collection is old, a minor collection
 * frees the young objects nothing reaches and finds an old object's young ones through the cards recorded stores
 * set, a full collection is still paced by the heap, and a minor collection that would keep most of what it
 * looks at is given up. Built a second time with NETWASM_GC_STRESS, the collector reports a store that was not
 * recorded. */
#include "../../src/NetWasm.Runtime/collector/netwasm/generational_gc.h"
#include "../../src/NetWasm.Runtime/collector/netwasm/reference_cards.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>

#define MIB ((size_t)1024 * 1024)
/* A small object that fills one 3,008-byte slot. */
#define LEAF_BYTES 3000u
#define SLOT 3008u
/* An object past the small limit has a region of its own, which goes back to the embedder when it dies: whether
 * the collector still contains its address tells whether it was kept. */
#define LARGE_BYTES 8192u

typedef struct {
    size_t fixed_bytes;
    const size_t *fixed_offsets;
    size_t fixed_reference_count;
    size_t element_size;
    const size_t *element_offsets;
    size_t element_reference_count;
} Type;

/* Every object starts with its type and its length, as the runtime's start with a type id and a length. */
typedef struct {
    const Type *type;
    size_t length;
} Header;

typedef struct {
    Header header;
    uintptr_t next;
} Node;

typedef struct {
    size_t value;
    uintptr_t reference;
    size_t tail;
} Pair;

static const size_t node_offsets[] = {offsetof(Node, next)};
static const size_t reference_offsets[] = {0};
static const Type leaf_type = {.fixed_bytes = LEAF_BYTES};
static const Type large_type = {.fixed_bytes = LARGE_BYTES};
static const Type node_type = {
    .fixed_bytes = sizeof(Node), .fixed_offsets = node_offsets, .fixed_reference_count = 1};
static const Type references_type = {
    .fixed_bytes = sizeof(Header), .element_size = sizeof(uintptr_t),
    .element_offsets = reference_offsets, .element_reference_count = 1};

void collector_object_layout(const void *object, NetWasmCollectorLayout *layout)
{
    const Header *header = object;
    const Type *type = header->type;
    assert(type != NULL);
    *layout = (NetWasmCollectorLayout){
        .fixed_offsets = type->fixed_offsets,
        .fixed_reference_count = type->fixed_reference_count,
        .fixed_bytes = type->fixed_bytes,
        .element_offsets = type->element_offsets,
        .element_reference_count = type->element_reference_count,
        .element_size = type->element_size,
        .element_count = header->length,
    };
}

typedef union {
    struct {
        size_t bytes;
    } fields;
    max_align_t alignment;
} AllocationHeader;

typedef struct {
    uintptr_t *values;
    size_t count;
} Roots;

static size_t outstanding_bytes;
static size_t completions;
static void (*on_completion)(NetWasmGc *gc);

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    AllocationHeader *header = malloc(sizeof(*header) + bytes);
    assert(header != NULL);
    header->fields.bytes = bytes;
    outstanding_bytes += bytes;
    return header + 1;
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)alignment;
    AllocationHeader *header = (AllocationHeader *)address - 1;
    assert(header->fields.bytes == bytes);
    outstanding_bytes -= bytes;
    free(header);
}

static void trace_roots(NetWasmGc *gc, void *state)
{
    Roots *roots = state;
    for (size_t index = 0; index < roots->count; index++)
        netwasm_gc_mark(gc, roots->values[index]);
}

static void complete_mark(NetWasmGc *gc, void *state)
{
    (void)state;
    completions++;
    if (on_completion != NULL) on_completion(gc);
}

static NetWasmGc *create(Roots *roots)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, MIB, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, roots);
    netwasm_gc_set_mark_completion(gc, complete_mark, NULL);
    completions = 0;
    on_completion = NULL;
    return gc;
}

static void *allocate(NetWasmGc *gc, const Type *type, size_t length)
{
    void *object = NULL;
    bool references = type->fixed_reference_count != 0 || (length != 0 && type->element_reference_count != 0);
    assert(netwasm_gc_allocate(gc, type->fixed_bytes + length * type->element_size, references, &object) ==
        NETWASM_GC_OK);
    Header *header = object;
    header->type = type;
    header->length = length;
    return object;
}

#ifndef NETWASM_GC_STRESS
static const size_t pair_offsets[] = {offsetof(Pair, reference)};
static const size_t wide_offsets[] = {8000};
static const Type pairs_type = {
    .fixed_bytes = sizeof(Header), .element_size = sizeof(Pair),
    .element_offsets = pair_offsets, .element_reference_count = 1};
/* A large object that is all fixed part, with a reference far from its start. */
static const Type wide_type = {
    .fixed_bytes = LARGE_BYTES, .fixed_offsets = wide_offsets, .fixed_reference_count = 1};

/* Allocates leaves until the heap has occupied bytes more, rooting keep of every period. */
static void fill(NetWasmGc *gc, Roots *roots, size_t bytes, size_t keep, size_t period)
{
    for (size_t index = 0; index < bytes / SLOT; index++) {
        void *object = allocate(gc, &leaf_type, 0);
        if (index % period < keep) roots->values[roots->count++] = (uintptr_t)object;
    }
}

/* Roots bytes of leaves and makes them old, half a budget at a time so that no collection comes due on the
 * way: one would be a minor collection over objects that are all alive, which is given up. */
static void build_old_heap(NetWasmGc *gc, Roots *roots, size_t bytes)
{
    for (size_t built = 0; built < bytes; built += MIB / 2) {
        fill(gc, roots, bytes - built < MIB / 2 ? bytes - built : MIB / 2, 1, 1);
        netwasm_gc_collect(gc);
    }
    assert(netwasm_gc_given_up_minor_collection_count(gc) == 0 && netwasm_gc_minor_collection_count(gc) == 0);
}

static uint64_t full_collections(const NetWasmGc *gc)
{
    return netwasm_gc_collection_count(gc) - netwasm_gc_minor_collection_count(gc);
}
/* Allocates garbage until one more minor collection has run, and checks that it was one. */
static void run_a_minor_collection(NetWasmGc *gc, Roots *roots)
{
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    uint64_t full = full_collections(gc);
    uint64_t abandoned = netwasm_gc_given_up_minor_collection_count(gc);
    for (size_t index = 0; netwasm_gc_minor_collection_count(gc) == minor; index++) {
        assert(index < 2 * MIB / SLOT);
        fill(gc, roots, SLOT, 0, 1);
    }
    assert(netwasm_gc_minor_collection_count(gc) == minor + 1 && full_collections(gc) == full &&
        netwasm_gc_given_up_minor_collection_count(gc) == abandoned);
}

static bool in_sink(const unsigned char *card)
{
    return card >= netwasm_gc_card_sink && card < netwasm_gc_card_sink + NWGC_CARD_LEAF_CARDS;
}

/* With no collector every address's card is in the sink, and a store through any address is harmless. */
static void a_store_outside_the_heap_lands_in_the_sink(void)
{
    uintptr_t local = 0;
    static uintptr_t fixed;
    assert(in_sink(netwasm_gc_card((uintptr_t)&local)) && in_sink(netwasm_gc_card((uintptr_t)&fixed)) &&
        in_sink(netwasm_gc_card(0)) && in_sink(netwasm_gc_card(UINTPTR_MAX)));
    netwasm_gc_record_reference_write((uintptr_t)&local);
    netwasm_gc_record_reference_write((uintptr_t)&fixed);
    netwasm_gc_record_reference_write(UINTPTR_MAX);
    assert(*netwasm_gc_card((uintptr_t)&local) == 1);
}

/* The collector's memory has leaves of its own, which start on a cache line; a range sets every card it
 * overlaps and no other; a collection that runs to its end clears them; the leaves go back with the collector;
 * and one collector with generations exists at a time. */
static void the_heap_has_cards_of_its_own(void)
{
    static uintptr_t values[512];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    NetWasmGc *second = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, MIB, &second) == NETWASM_GC_INVALID_ARGUMENT);

    build_old_heap(gc, &roots, MIB);
    unsigned char *array = allocate(gc, &references_type, 4096);
    roots.values[roots.count++] = (uintptr_t)array;
    netwasm_gc_collect(gc);
    uintptr_t first = (uintptr_t)array;
    assert(!in_sink(netwasm_gc_card(first)));
    uintptr_t span = first & ~(NWGC_CARD_SPAN_BYTES - 1);
    assert(((uintptr_t)netwasm_gc_card(span) & (NWGC_CARD_LINE_BYTES - 1)) == 0);
    assert(netwasm_gc_card(span + NWGC_CARD_BYTES) == netwasm_gc_card(span) + 1);

    /* From the middle of one card to the first byte of the fourth after it: five cards. */
    uintptr_t start = (first + 8 * NWGC_CARD_BYTES) & ~(NWGC_CARD_BYTES - 1);
    for (uintptr_t card = start - NWGC_CARD_BYTES; card <= start + 6 * NWGC_CARD_BYTES; card += NWGC_CARD_BYTES)
        assert(*netwasm_gc_card(card) == 0);
    netwasm_gc_record_reference_writes(start + 100, 4 * NWGC_CARD_BYTES - 99);
    assert(*netwasm_gc_card(start - NWGC_CARD_BYTES) == 0);
    for (size_t card = 0; card < 5; card++) assert(*netwasm_gc_card(start + card * NWGC_CARD_BYTES) == 1);
    assert(*netwasm_gc_card(start + 5 * NWGC_CARD_BYTES) == 0);
    netwasm_gc_record_reference_writes(start + 7 * NWGC_CARD_BYTES, 0);
    assert(*netwasm_gc_card(start + 7 * NWGC_CARD_BYTES) == 0);

    run_a_minor_collection(gc, &roots);
    for (size_t card = 0; card < 5; card++) assert(*netwasm_gc_card(start + card * NWGC_CARD_BYTES) == 0);
    netwasm_gc_record_reference_write(first);
    netwasm_gc_collect(gc);
    assert(*netwasm_gc_card(first) == 0);

    netwasm_gc_destroy(gc);
    assert(outstanding_bytes == 0 && in_sink(netwasm_gc_card(first)));
    gc = create(&roots);
    netwasm_gc_destroy(gc);
}

/* A large object without references never takes a store of one and gets no cards: a store through an address in
 * it lands in the sink. One with references gets a leaf for every span it covers, on a cache line and each its
 * own, past what the slab taken when the collector started holds (seven spans of 4 MiB where addresses are 64
 * bits, 31 of 1 MiB on wasm32), so that a later slab is taken too. */
static void only_a_large_object_with_references_gets_cards(void)
{
    static uintptr_t values[4];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    const size_t spans = 40;
    void *bytes = NULL;
    assert(netwasm_gc_allocate(gc, (spans + 1) * NWGC_CARD_SPAN_BYTES, false, &bytes) == NETWASM_GC_OK);
    uintptr_t inside = ((uintptr_t)bytes + NWGC_CARD_SPAN_BYTES) & ~(NWGC_CARD_SPAN_BYTES - 1);
    for (size_t span = 0; span < spans; span++) assert(in_sink(netwasm_gc_card(inside + span * NWGC_CARD_SPAN_BYTES)));
    netwasm_gc_record_reference_write(inside);

    unsigned char *array = allocate(gc, &references_type, (spans + 1) * NWGC_CARD_SPAN_BYTES / sizeof(uintptr_t));
    inside = ((uintptr_t)array + NWGC_CARD_SPAN_BYTES) & ~(NWGC_CARD_SPAN_BYTES - 1);
    for (size_t span = 0; span < spans; span++) {
        const unsigned char *card = netwasm_gc_card(inside + span * NWGC_CARD_SPAN_BYTES);
        assert(!in_sink(card) && ((uintptr_t)card & (NWGC_CARD_LINE_BYTES - 1)) == 0);
        if (span != 0) assert(card != netwasm_gc_card(inside + (span - 1) * NWGC_CARD_SPAN_BYTES));
    }
    netwasm_gc_destroy(gc);
    assert(in_sink(netwasm_gc_card(inside)));
}

/* Garbage that dies young is freed by minor collections, one each time the heap has grown by the minimum
 * budget; the old objects stay, and no full collection is needed while the heap stays under its charge. */
static void young_garbage_is_freed_by_minor_collections(void)
{
    static uintptr_t values[1024];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    build_old_heap(gc, &roots, 2 * MIB);
    uint64_t full = full_collections(gc);
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    size_t old = netwasm_gc_object_count(gc);
    assert(old == roots.count);
    for (size_t index = 0; index < roots.count; index++) assert(netwasm_gc_is_marked(gc, roots.values[index]));

    size_t kept = roots.count;
    fill(gc, &roots, 8 * MIB, 0, 1);
    assert(roots.count == kept);
    uint64_t ran = netwasm_gc_minor_collection_count(gc) - minor;
    assert(ran >= 7 && ran <= 8);
    assert(full_collections(gc) == full && netwasm_gc_given_up_minor_collection_count(gc) == 0);
    assert(netwasm_gc_object_count(gc) >= old && netwasm_gc_object_count(gc) <= old + MIB / SLOT + 2);
    for (size_t index = 0; index < roots.count; index++) assert(netwasm_gc_contains(gc, roots.values[index]));
    /* A young object is not marked until a collection has kept it: generation 0, then the oldest. */
    void *young = allocate(gc, &leaf_type, 0);
    assert(!netwasm_gc_is_marked(gc, (uintptr_t)young));
    assert(netwasm_gc_object_generation(gc, (uintptr_t)young) == 0);
    assert(netwasm_gc_object_generation(gc, roots.values[0]) == 1);
    assert(netwasm_gc_object_generation(gc, 0) == 0);
    assert(netwasm_gc_object_generation(NULL, roots.values[0]) == 0);
    /* The oldest generation is collected only by collections of every object. */
    assert(netwasm_gc_oldest_generation_collection_count(gc) == full_collections(gc));
    assert(netwasm_gc_oldest_generation_collection_count(gc) + netwasm_gc_minor_collection_count(gc) ==
        netwasm_gc_collection_count(gc));
    assert(netwasm_gc_oldest_generation_collection_count(NULL) == 0);
    roots.values[roots.count++] = (uintptr_t)young;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_generation(gc, (uintptr_t)young) == 1);
    netwasm_gc_destroy(gc);
}

/* An old object's reference to a young one is found through the card its store set: at the slot, or at the
 * start of the object stored into. A store that set no card is not seen, which is why every store sets one. */
static void a_recorded_store_keeps_a_young_object(void)
{
    static uintptr_t values[512];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    /* With little alive a full collection costs next to nothing and a minor one cannot beat it, so the old
     * heap here is a budget's worth. */
    build_old_heap(gc, &roots, MIB);
    Node *holder = allocate(gc, &node_type, 0);
    Node *other = allocate(gc, &node_type, 0);
    roots.values[roots.count++] = (uintptr_t)holder;
    roots.values[roots.count++] = (uintptr_t)other;
    netwasm_gc_collect(gc);

    void *by_slot = allocate(gc, &large_type, 0);
    void *by_object = allocate(gc, &large_type, 0);
    holder->next = (uintptr_t)by_slot;
    netwasm_gc_record_reference_write((uintptr_t)&holder->next);
    other->next = (uintptr_t)by_object;
    netwasm_gc_record_reference_write((uintptr_t)other);
    run_a_minor_collection(gc, &roots);
    assert(netwasm_gc_contains(gc, (uintptr_t)by_slot) && netwasm_gc_contains(gc, (uintptr_t)by_object));
    assert(netwasm_gc_is_marked(gc, (uintptr_t)by_slot));

    void *unrecorded = allocate(gc, &large_type, 0);
    holder->next = (uintptr_t)unrecorded;
    run_a_minor_collection(gc, &roots);
    assert(!netwasm_gc_contains(gc, (uintptr_t)unrecorded));
    holder->next = 0;
    netwasm_gc_destroy(gc);
}

/* GC.Collect(0): a minor collection, run to its end however much it marks, that frees young garbage and keeps old
 * garbage, even as the program's first collection; after a minor collection was given up, the collection of every
 * object that must come next. */
static void collecting_the_young_generation_is_a_minor_collection(void)
{
    static uintptr_t values[2048];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);

    /* The first: every object is young and what survives is old; the first collection of every object is still
     * due when the heap has grown by the minimum budget. */
    void *kept = allocate(gc, &leaf_type, 0);
    roots.values[roots.count++] = (uintptr_t)kept;
    void *dropped = allocate(gc, &leaf_type, 0);
    netwasm_gc_collect_young(gc);
    assert(full_collections(gc) == 0 && netwasm_gc_minor_collection_count(gc) == 1);
    assert(netwasm_gc_contains(gc, (uintptr_t)kept) && !netwasm_gc_contains(gc, (uintptr_t)dropped));
    assert(netwasm_gc_object_generation(gc, (uintptr_t)kept) == 1);
    fill(gc, &roots, MIB / 4, 0, 1);
    assert(netwasm_gc_collection_count(gc) == 1);
    roots.count = 0;
    netwasm_gc_destroy(gc);
    gc = create(&roots);

    build_old_heap(gc, &roots, MIB);
    size_t old = netwasm_gc_object_count(gc);
    roots.count--;
    void *young = allocate(gc, &leaf_type, 0);
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    uint64_t full = full_collections(gc);
    netwasm_gc_collect_young(gc);
    assert(netwasm_gc_minor_collection_count(gc) == minor + 1 && full_collections(gc) == full);
    assert(!netwasm_gc_contains(gc, (uintptr_t)young) && netwasm_gc_object_count(gc) == old);

    /* Young objects that all stay alive: an allocation's minor collection is given up, yet this one runs on. */
    fill(gc, &roots, MIB / 2, 1, 1);
    minor = netwasm_gc_minor_collection_count(gc);
    netwasm_gc_collect_young(gc);
    assert(netwasm_gc_minor_collection_count(gc) == minor + 1 &&
        netwasm_gc_given_up_minor_collection_count(gc) == 0);

    /* After one given up, every object. */
    fill(gc, &roots, MIB / 4 + MIB / 16, 1, 1);
    assert(!netwasm_gc_collect_when_idle(gc) && netwasm_gc_given_up_minor_collection_count(gc) == 1);
    full = full_collections(gc);
    netwasm_gc_collect_young(gc);
    assert(full_collections(gc) == full + 1);
    netwasm_gc_destroy(gc);
}

/* A minor collection's sweep leaves alone the old blocks nothing was allocated into, yet still traces, and then
 * clears, a card a store set over one; a collection of every object then finds the old objects and the young one
 * the store kept, and nothing else. */
static void a_minor_collection_leaves_unchanged_blocks_as_they_stand(void)
{
    static uintptr_t values[512];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    build_old_heap(gc, &roots, MIB);
    Node *holder = allocate(gc, &node_type, 0);
    roots.values[roots.count++] = (uintptr_t)holder;
    netwasm_gc_collect(gc);
    size_t objects = netwasm_gc_object_count(gc);
    uint64_t live = netwasm_gc_live_bytes(gc);

    void *young = allocate(gc, &large_type, 0);
    holder->next = (uintptr_t)young;
    netwasm_gc_record_reference_write((uintptr_t)&holder->next);
    assert(*netwasm_gc_card((uintptr_t)&holder->next) == 1);
    run_a_minor_collection(gc, &roots);
    assert(netwasm_gc_contains(gc, (uintptr_t)young) && *netwasm_gc_card((uintptr_t)&holder->next) == 0);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == objects + 1 && netwasm_gc_live_bytes(gc) == live + LARGE_BYTES);
    holder->next = 0;
    netwasm_gc_destroy(gc);
}

/* Blocks start where their arena puts them, not on a card, so the card of a block's last object can reach past
 * the block's end. A store into the last object of each block of old objects keeps its young object, and the minor
 * collection clears every card from that object to the next block's first, so none is read again. */
static void a_card_at_a_blocks_end_is_cleared(void)
{
    enum { NODES = 10000 };
    static uintptr_t values[NODES];
    static Node *nodes[NODES];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    for (size_t index = 0; index < NODES; index++) {
        nodes[index] = allocate(gc, &node_type, 0);
        roots.values[roots.count++] = (uintptr_t)nodes[index];
    }
    netwasm_gc_collect(gc);
    size_t slot = (size_t)((uintptr_t)nodes[1] - (uintptr_t)nodes[0]);
    size_t ends = 0;
    for (size_t index = 0; index + 1 < NODES; index++) {
        if ((uintptr_t)nodes[index + 1] - (uintptr_t)nodes[index] == slot) continue;
        nodes[index]->next = (uintptr_t)allocate(gc, &node_type, 0);
        netwasm_gc_record_reference_write((uintptr_t)&nodes[index]->next);
        ends++;
    }
    assert(ends >= 3);
    uint64_t full = full_collections(gc);
    netwasm_gc_collect_young(gc);
    assert(full_collections(gc) == full);
    for (size_t index = 0; index + 1 < NODES; index++) {
        if ((uintptr_t)nodes[index + 1] - (uintptr_t)nodes[index] == slot) continue;
        assert(netwasm_gc_contains(gc, nodes[index]->next) && netwasm_gc_is_marked(gc, nodes[index]->next));
        uintptr_t last = (uintptr_t)nodes[index + 1] + slot - 1;
        for (uintptr_t card = (uintptr_t)nodes[index] & ~(NWGC_CARD_BYTES - 1); card <= last; card += NWGC_CARD_BYTES)
            assert(*netwasm_gc_card(card) == 0);
        nodes[index]->next = 0;
    }
    netwasm_gc_destroy(gc);
}

/* A large array is traced only where its cards are set: the element written to is seen, and one far from it is
 * not. An element that overlaps a set card is traced whole, so a store recorded at an element's start covers a
 * reference of that element on the next card. A large object's fixed part is traced whole when a card over any
 * of it is set. */
static void a_large_object_is_traced_where_it_was_written(void)
{
    uintptr_t values[4];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    size_t length = 65536;
    /* An allocation this large makes a collection due, so each object is rooted, and made old, before the
     * next: a minor collection over an array that is alive would be given up. */
    Header *references = allocate(gc, &references_type, length);
    roots.values[roots.count++] = (uintptr_t)references;
    netwasm_gc_collect(gc);
    Header *pairs = allocate(gc, &pairs_type, length);
    roots.values[roots.count++] = (uintptr_t)pairs;
    netwasm_gc_collect(gc);
    unsigned char *wide = allocate(gc, &wide_type, 0);
    roots.values[roots.count++] = (uintptr_t)wide;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_given_up_minor_collection_count(gc) == 0);
    uintptr_t *slots = (uintptr_t *)(references + 1);
    Pair *elements = (Pair *)(pairs + 1);

    void *written = allocate(gc, &large_type, 0);
    void *last = allocate(gc, &large_type, 0);
    void *elsewhere = allocate(gc, &large_type, 0);
    slots[10] = (uintptr_t)written;
    netwasm_gc_record_reference_write((uintptr_t)&slots[10]);
    slots[length - 1] = (uintptr_t)last;
    netwasm_gc_record_reference_write((uintptr_t)&slots[length - 1]);
    slots[40000] = (uintptr_t)elsewhere;

    /* An element that starts on one card and holds its reference on the next. */
    size_t straddling = 0;
    while ((((uintptr_t)&elements[straddling]) >> NWGC_CARD_SHIFT) ==
        (((uintptr_t)&elements[straddling].reference) >> NWGC_CARD_SHIFT)) straddling++;
    void *by_element = allocate(gc, &large_type, 0);
    elements[straddling].reference = (uintptr_t)by_element;
    netwasm_gc_record_reference_write((uintptr_t)&elements[straddling]);

    void *by_start = allocate(gc, &large_type, 0);
    uintptr_t reference = (uintptr_t)by_start;
    memcpy(wide + wide_offsets[0], &reference, sizeof(reference));
    netwasm_gc_record_reference_write((uintptr_t)wide);

    run_a_minor_collection(gc, &roots);
    assert(netwasm_gc_contains(gc, (uintptr_t)written) && netwasm_gc_contains(gc, (uintptr_t)last));
    assert(!netwasm_gc_contains(gc, (uintptr_t)elsewhere));
    assert(netwasm_gc_contains(gc, (uintptr_t)by_element) && netwasm_gc_contains(gc, (uintptr_t)by_start));
    slots[40000] = 0;

    /* A full collection reads no cards: it traces the arrays whole. */
    void *found = allocate(gc, &large_type, 0);
    slots[40000] = (uintptr_t)found;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_contains(gc, (uintptr_t)found));
    netwasm_gc_destroy(gc);
}

static uintptr_t completion_old;
static uintptr_t completion_dead;
static uintptr_t completion_kept;
static bool completion_saw_old_marked;
static bool completion_saw_dead_marked;

static void keep_one_young_object(NetWasmGc *gc)
{
    completion_saw_old_marked = netwasm_gc_is_marked(gc, completion_old);
    completion_saw_dead_marked = netwasm_gc_is_marked(gc, completion_dead);
    netwasm_gc_mark(gc, completion_kept);
    netwasm_gc_drain(gc);
}

/* Completing the mark, where weak references are cleared and finalizers queued, runs in a minor collection as
 * in a full one: old objects are marked there, an unreachable young one is not, and what it marks is kept. */
static void a_minor_collection_completes_its_mark(void)
{
    static uintptr_t values[512];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    build_old_heap(gc, &roots, MIB);
    completion_old = (uintptr_t)allocate(gc, &large_type, 0);
    roots.values[roots.count++] = completion_old;
    netwasm_gc_collect(gc);
    completion_dead = (uintptr_t)allocate(gc, &large_type, 0);
    completion_kept = (uintptr_t)allocate(gc, &large_type, 0);
    size_t before = completions;
    on_completion = keep_one_young_object;
    run_a_minor_collection(gc, &roots);
    on_completion = NULL;
    assert(completions == before + 1 && completion_saw_old_marked && !completion_saw_dead_marked);
    assert(netwasm_gc_contains(gc, completion_kept) && !netwasm_gc_contains(gc, completion_dead));
    netwasm_gc_destroy(gc);
}

/* Objects that survive a minor collection and die later are old garbage, which only a full collection frees:
 * the heap grows to the charge the pacing rule set at the last full collection, and a full one runs there. */
static void old_garbage_waits_for_a_full_collection(void)
{
    static uintptr_t values[8192];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    build_old_heap(gc, &roots, MIB);
    size_t kept = roots.count;
    for (size_t index = 0; index < 64; index++) roots.values[roots.count++] = 0;
    uint64_t full = full_collections(gc);
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    /* One object in ten stays rooted for the next 640 allocations, 1.8 MiB: past a minor collection. */
    uint64_t most = 0;
    for (size_t index = 0; index < 40 * MIB / SLOT; index++) {
        void *object = allocate(gc, &leaf_type, 0);
        if (index % 10 == 0) roots.values[kept + index / 10 % 64] = (uintptr_t)object;
        if (netwasm_gc_live_bytes(gc) > most) most = netwasm_gc_live_bytes(gc);
    }
    assert(netwasm_gc_minor_collection_count(gc) > minor + 20);
    assert(full_collections(gc) > full && netwasm_gc_given_up_minor_collection_count(gc) == 0);
    /* The charge after 1 MiB survived whole is 4 MiB, and later charges are lower: the heap never passed it by
     * more than the run an allocation takes at once. */
    assert(most <= 4 * MIB + 2 * SLOT);
    netwasm_gc_destroy(gc);
}

/* A minor collection that finds most of the young objects alive is given up: nothing is freed or lost, the
 * heap is counted exactly, completing the mark does not run, and the next collection is a full one at the
 * heap's charge. Minor collections stay off until the first full collection after four minimum budgets of
 * allocation, and after sixteen when the next one is given up as well. */
static void a_minor_collection_that_keeps_most_is_given_up(void)
{
    static uintptr_t values[16384];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    build_old_heap(gc, &roots, MIB);
    uint64_t full = full_collections(gc);
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    size_t completed = completions;

    /* A second MiB, all of it alive, makes the minor collection due and has it given up. */
    while (netwasm_gc_given_up_minor_collection_count(gc) == 0) {
        assert(roots.count < 2 * MIB / SLOT + 8);
        fill(gc, &roots, SLOT, 1, 1);
    }
    assert(netwasm_gc_minor_collection_count(gc) == minor && full_collections(gc) == full);
    assert(completions == completed);
    assert(netwasm_gc_object_count(gc) == roots.count);
    assert(netwasm_gc_live_bytes(gc) == (uint64_t)roots.count * SLOT);
    for (size_t index = 0; index < roots.count; index++) assert(netwasm_gc_contains(gc, roots.values[index]));

    /* Garbage from here. The heap's charge after 1 MiB survived whole is 4 MiB: no collection before it, and a
     * full one there, about 2 MiB of allocation after the minor collection was given up. */
    size_t alive = roots.count;
    fill(gc, &roots, MIB, 0, 1);
    assert(netwasm_gc_collection_count(gc) == full + minor);
    fill(gc, &roots, MIB + MIB / 4, 0, 1);
    assert(full_collections(gc) == full + 1 && netwasm_gc_minor_collection_count(gc) == minor);
    assert(netwasm_gc_object_count(gc) <= alive + MIB / (4 * SLOT) + 2);
    /* Four budgets have not been allocated yet, so the next collection is full again: 2 MiB survived of 4, so
     * it is due at 5 MiB, 3 MiB of garbage from here. */
    fill(gc, &roots, 2 * MIB + MIB / 2, 0, 1);
    assert(full_collections(gc) == full + 1 && netwasm_gc_minor_collection_count(gc) == minor);
    fill(gc, &roots, MIB, 0, 1);
    assert(full_collections(gc) == full + 2 && netwasm_gc_minor_collection_count(gc) == minor);
    /* That full collection came after the four budgets, so minor collections are back. */
    run_a_minor_collection(gc, &roots);
    assert(netwasm_gc_given_up_minor_collection_count(gc) == 1);
    netwasm_gc_destroy(gc);
}

/* A minor collection that runs to its end starts the wait over; two given up in a row make it sixteen budgets. */
static void the_wait_grows_while_minor_collections_keep_being_given_up(void)
{
    static uintptr_t values[32768];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    build_old_heap(gc, &roots, MIB);
    /* The first is given up, as above. */
    while (netwasm_gc_given_up_minor_collection_count(gc) == 0) fill(gc, &roots, SLOT, 1, 1);
    /* Garbage until minor collections are back: the first full collection after four budgets. */
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    size_t allocated = 0;
    while (netwasm_gc_minor_collection_count(gc) == minor) {
        assert(allocated < 16 * MIB);
        fill(gc, &roots, SLOT, 0, 1);
        allocated += SLOT;
    }
    assert(allocated >= 4 * MIB - MIB / 4 && netwasm_gc_given_up_minor_collection_count(gc) == 1);

    /* That minor collection ran to its end, so one given up now waits four budgets again, not sixteen. */
    while (netwasm_gc_given_up_minor_collection_count(gc) == 1) fill(gc, &roots, SLOT, 1, 1);
    minor = netwasm_gc_minor_collection_count(gc);
    /* The next is given up before any minor collection has run to its end: all of what is allocated stays
     * alive, so once minor collections are back the first one is given up too. */
    while (netwasm_gc_given_up_minor_collection_count(gc) == 2) {
        assert(roots.count < 32000);
        fill(gc, &roots, SLOT, 1, 1);
    }
    assert(netwasm_gc_minor_collection_count(gc) == minor);
    /* Now the wait is sixteen budgets: garbage until a minor collection runs. */
    allocated = 0;
    while (netwasm_gc_minor_collection_count(gc) == minor) {
        assert(allocated < 64 * MIB);
        fill(gc, &roots, SLOT, 0, 1);
        allocated += SLOT;
    }
    assert(allocated >= 16 * MIB - MIB / 4 && netwasm_gc_given_up_minor_collection_count(gc) == 3);
    netwasm_gc_destroy(gc);
}

/* An idle embedder's collection is the one the heap is heading for: a minor one while those pay. */
static void an_idle_collection_is_a_minor_one(void)
{
    static uintptr_t values[1024];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    fill(gc, &roots, MIB / 2, 1, 1);
    netwasm_gc_collect(gc);
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    uint64_t full = full_collections(gc);
    assert(!netwasm_gc_collect_when_idle(gc));
    fill(gc, &roots, MIB / 2, 0, 1);
    assert(netwasm_gc_collect_when_idle(gc));
    assert(netwasm_gc_minor_collection_count(gc) == minor + 1 && full_collections(gc) == full);
    /* A quiet embedder's collects everything, old garbage included. */
    roots.count = 0;
    assert(netwasm_gc_collect_when_quiet(gc) != 0);
    assert(full_collections(gc) == full + 1 && netwasm_gc_object_count(gc) == 0);
    netwasm_gc_destroy(gc);
}

/* A minor collection costs what was allocated since the last collection, so an idle period takes it once the
 * heap has grown by the budget's share (a quarter), though by much less than half of what is old; one given up is no
 * collection, and the embedder is told so. */
static void an_idle_minor_collection_needs_only_the_budgets_share(void)
{
    static uintptr_t values[2048];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    build_old_heap(gc, &roots, 2 * MIB);
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    uint64_t full = full_collections(gc);
    fill(gc, &roots, MIB / 4 + MIB / 16, 0, 1);
    assert(netwasm_gc_collect_when_idle(gc));
    assert(netwasm_gc_minor_collection_count(gc) == minor + 1 && full_collections(gc) == full);

    /* Young objects that all stay alive: the minor collection marks past its limit and is given up. */
    uint64_t collections = netwasm_gc_collection_count(gc);
    uint64_t abandoned = netwasm_gc_given_up_minor_collection_count(gc);
    fill(gc, &roots, MIB / 4 + MIB / 16, 1, 1);
    assert(!netwasm_gc_collect_when_idle(gc));
    assert(netwasm_gc_collection_count(gc) == collections &&
        netwasm_gc_given_up_minor_collection_count(gc) == abandoned + 1);
    netwasm_gc_destroy(gc);
}
#else
static const void *missed_holder;
static const void *missed_slot;
static uintptr_t missed_reference;
static size_t missed;

void collector_missed_reference_write(const void *holder, const void *slot, uintptr_t reference)
{
    missed_holder = holder;
    missed_slot = slot;
    missed_reference = reference;
    missed++;
}

/* Allocates until the next two collections are minor ones: qualification makes every eighth a full one, and
 * each case below allocates twice, a young object and then what collects it. */
static void before_two_minor_collections(NetWasmGc *gc)
{
    while (netwasm_gc_collection_count(gc) % 8 >= 6) allocate(gc, &leaf_type, 0);
}

/* Qualification collects before every allocation and checks each minor collection: a reference an old object
 * holds to a young one that no recorded store accounts for is reported with its holder and slot, in a small
 * object and in an element of a large array alike. A recorded store is not. */
static void an_unrecorded_store_is_reported(void)
{
    uintptr_t values[4];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(&roots);
    /* Every allocation collects first, so each object is rooted before the next is allocated. */
    Node *holder = allocate(gc, &node_type, 0);
    roots.values[roots.count++] = (uintptr_t)holder;
    Header *references = allocate(gc, &references_type, 4096);
    roots.values[roots.count++] = (uintptr_t)references;
    uintptr_t *slots = (uintptr_t *)(references + 1);
    netwasm_gc_collect(gc);

    before_two_minor_collections(gc);
    void *young = allocate(gc, &large_type, 0);
    holder->next = (uintptr_t)young;
    netwasm_gc_record_reference_write((uintptr_t)&holder->next);
    uint64_t minor = netwasm_gc_minor_collection_count(gc);
    allocate(gc, &leaf_type, 0);
    assert(netwasm_gc_minor_collection_count(gc) == minor + 1 && missed == 0);
    assert(netwasm_gc_contains(gc, (uintptr_t)young));

    before_two_minor_collections(gc);
    young = allocate(gc, &large_type, 0);
    holder->next = (uintptr_t)young;
    allocate(gc, &leaf_type, 0);
    assert(missed == 1 && missed_holder == holder && missed_slot == &holder->next &&
        missed_reference == (uintptr_t)young);
    holder->next = 0;

    before_two_minor_collections(gc);
    young = allocate(gc, &large_type, 0);
    slots[3000] = (uintptr_t)young;
    netwasm_gc_record_reference_write((uintptr_t)&slots[10]);
    allocate(gc, &leaf_type, 0);
    assert(missed == 2 && missed_holder == references && missed_slot == &slots[3000] &&
        missed_reference == (uintptr_t)young);
    slots[3000] = 0;
    assert(completions != 0);
    netwasm_gc_destroy(gc);
}
#endif

int main(void)
{
#ifndef NETWASM_GC_STRESS
    a_store_outside_the_heap_lands_in_the_sink();
    the_heap_has_cards_of_its_own();
    only_a_large_object_with_references_gets_cards();
    young_garbage_is_freed_by_minor_collections();
    a_recorded_store_keeps_a_young_object();
    a_minor_collection_leaves_unchanged_blocks_as_they_stand();
    collecting_the_young_generation_is_a_minor_collection();
    a_card_at_a_blocks_end_is_cleared();
    a_large_object_is_traced_where_it_was_written();
    a_minor_collection_completes_its_mark();
    old_garbage_waits_for_a_full_collection();
    a_minor_collection_that_keeps_most_is_given_up();
    the_wait_grows_while_minor_collections_keep_being_given_up();
    an_idle_collection_is_a_minor_one();
    an_idle_minor_collection_needs_only_the_budgets_share();
#else
    an_unrecorded_store_is_reported();
#endif
    assert(outstanding_bytes == 0);
    return 0;
}
