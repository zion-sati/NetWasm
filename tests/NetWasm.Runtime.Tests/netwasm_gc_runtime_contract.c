/* SPDX-License-Identifier: MIT */
#include "collector_allocation.h"
#include "collector_collection.h"
#include "collector_ephemeron.h"
#include "collector_finalization.h"
#include "collector_identity_hash.h"
#include "collector_lifecycle.h"
#include "collector_metadata.h"
#include "collector_pinning.h"
#include "collector_reference_store.h"
#include "collector_roots.h"
#include "collector_weak_reference.h"
#include "netwasm/runtime_state.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

void runtime_initialize_libc(void) { }

/* The runtime's part: each object's layout, as it would read it from a type id. */
typedef struct {
    uintptr_t object;
    NetWasmCollectorLayout layout;
} RecordedLayout;

static RecordedLayout recorded[256];
static size_t recorded_count;
/* Bulk tests allocate more objects than the record holds, all of them eight atomic bytes. */
static bool unrecorded_objects_are_atomic;

void collector_object_layout(const void *object, NetWasmCollectorLayout *layout)
{
    for (size_t index = 0; index < recorded_count; index++) {
        if (recorded[index].object == (uintptr_t)object) {
            *layout = recorded[index].layout;
            return;
        }
    }
    assert(unrecorded_objects_are_atomic && "object has no recorded layout");
    *layout = (NetWasmCollectorLayout){.fixed_bytes = 8};
}

static void *allocate_with_layout(NetWasmCollectorLayout layout)
{
    size_t bytes = layout.fixed_bytes + layout.element_count * layout.element_size;
    void *object = collector_allocate_object(bytes,
        layout.fixed_reference_count != 0 ||
            (layout.element_reference_count != 0 && layout.element_count != 0));
    assert(object != NULL);
    size_t index = 0;
    while (index < recorded_count && recorded[index].object != (uintptr_t)object) index++;
    if (index == recorded_count) {
        assert(recorded_count < sizeof(recorded) / sizeof(recorded[0]));
        recorded_count++;
    }
    recorded[index] = (RecordedLayout){(uintptr_t)object, layout};
    return object;
}

static void *allocate_atomic(size_t bytes)
{
    return allocate_with_layout((NetWasmCollectorLayout){.fixed_bytes = bytes});
}

static const size_t first_word[] = {0};

static uintptr_t frame_root;
static size_t finalized;

static void enumerate_roots(void)
{
    collector_trace_root_range(&frame_root, &frame_root + 1);
}

static void finalize(void *object, void *context)
{
    assert(context == &finalized);
    assert(netwasm_gc_contains(netwasm_gc_runtime, (uintptr_t)object));
    finalized++;
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, (uintptr_t)object));
}

static void allocation_and_roots(void)
{
    uintptr_t unrooted = (uintptr_t)allocate_atomic(32);
    assert(unrooted != 0);
    collector_collect();
    assert(!netwasm_gc_contains(netwasm_gc_runtime, unrooted));

    collector_set_root_enumerator(enumerate_roots);
    frame_root = (uintptr_t)allocate_atomic(32);
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, frame_root));
    frame_root = 0;
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);

    uintptr_t roots[2] = {
        (uintptr_t)allocate_atomic(8),
        (uintptr_t)allocate_atomic(8),
    };
    collector_register_roots(roots, sizeof(roots));
    collector_register_roots(roots, sizeof(roots));
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 2);
    collector_unregister_roots(roots, sizeof(roots));
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 2);
    collector_unregister_roots(roots, sizeof(roots));
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

static void layouts_and_ranges(void)
{
    /* A reference, then an integer that only looks like one. */
    uintptr_t *parent = allocate_with_layout((NetWasmCollectorLayout){
        .fixed_offsets = first_word,
        .fixed_reference_count = 1,
        .fixed_bytes = 2 * sizeof(uintptr_t),
    });
    uintptr_t reference = (uintptr_t)allocate_atomic(8);
    uintptr_t integer = (uintptr_t)allocate_atomic(8);
    parent[0] = reference;
    parent[1] = integer;
    frame_root = (uintptr_t)parent;
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, reference));
    assert(!netwasm_gc_contains(netwasm_gc_runtime, integer));
    frame_root = 0;
    collector_collect();

    uintptr_t *array = allocate_with_layout((NetWasmCollectorLayout){
        .element_offsets = first_word,
        .element_reference_count = 1,
        .element_size = sizeof(uintptr_t),
        .element_count = 3,
    });
    for (size_t index = 0; index < 3; index++)
        array[index] = (uintptr_t)allocate_atomic(8);
    uintptr_t endpoint = (uintptr_t)(array + 3);
    collector_register_root_range(&endpoint, &endpoint + 1);
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 4);
    collector_unregister_root_range(&endpoint, &endpoint + 1);
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

static void lifetime_contract(void)
{
    uintptr_t key = (uintptr_t)allocate_atomic(8);
    uintptr_t value = (uintptr_t)allocate_atomic(8);
    uintptr_t short_slot = key;
    uintptr_t long_slot = key;
    collector_register_finalizer((void *)key, finalize, &finalized, NULL, NULL);
    assert(collector_register_short_weak_reference(&short_slot, key));
    assert(collector_register_weak_reference(
        &long_slot,
        key,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION));
    assert(collector_register_ephemeron(&key, &value));
    collector_collect();
    assert(short_slot == 0);
    assert(long_slot == key);
    assert(netwasm_gc_contains(netwasm_gc_runtime, value));
    assert(collector_has_pending_finalizers());
    assert(collector_invoke_finalizers() == 1);
    assert(finalized == 1);
    assert(!collector_has_pending_finalizers());
    collector_collect();
    assert(key == 0 && value == 0 && long_slot == 0);
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

/* A finalizer may suppress another object queued beside its own, as GC.SuppressFinalize can. */
static void suppressed_queued_finalizers_are_no_longer_pending(void)
{
    uintptr_t first = (uintptr_t)allocate_atomic(8);
    uintptr_t second = (uintptr_t)allocate_atomic(8);
    size_t before = finalized;
    assert(!collector_has_pending_finalizers());
    collector_register_finalizer((void *)first, finalize, &finalized, NULL, NULL);
    collector_register_finalizer((void *)second, finalize, &finalized, NULL, NULL);
    assert(!collector_has_pending_finalizers());
    collector_collect();
    assert(collector_has_pending_finalizers());
    collector_register_finalizer((void *)first, NULL, NULL, NULL, NULL);
    assert(collector_has_pending_finalizers());
    collector_register_finalizer((void *)second, NULL, NULL, NULL, NULL);
    assert(!collector_has_pending_finalizers());
    assert(collector_invoke_finalizers() == 0);
    assert(finalized == before);
    collector_collect();
    assert(!collector_has_pending_finalizers());
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

static size_t counted;

static void count_finalization(void *object, void *context)
{
    (void)object;
    (void)context;
    counted++;
}

static NetWasmCollectorFinalizer reregistering_finalizer;
static size_t reregistrations;

/* ReRegisterForFinalize from its own finalizer: the object finalizes again later. */
static void reregister_once(void *object, void *context)
{
    counted++;
    if (reregistrations++ == 0)
        collector_register_finalizer(object, reregistering_finalizer, context, NULL, NULL);
}

static uintptr_t suppressed_by_finalizer;

static void suppress_other(void *object, void *context)
{
    (void)object;
    (void)context;
    counted++;
    collector_register_finalizer((void *)suppressed_by_finalizer, NULL, NULL, NULL, NULL);
}

static void finalizers_reregister_and_suppress_while_running(void)
{
    counted = 0;
    reregistering_finalizer = reregister_once;
    uintptr_t again = (uintptr_t)allocate_atomic(8);
    collector_register_finalizer((void *)again, reregister_once, NULL, NULL, NULL);
    collector_collect();
    assert(collector_invoke_finalizers() == 1 && counted == 1);
    assert(!collector_has_pending_finalizers());
    collector_collect();
    assert(collector_invoke_finalizers() == 1 && counted == 2);

    /* The first queued finalizer suppresses the second, which never runs. */
    uintptr_t first = (uintptr_t)allocate_atomic(8);
    suppressed_by_finalizer = (uintptr_t)allocate_atomic(8);
    collector_register_finalizer((void *)first, suppress_other, NULL, NULL, NULL);
    collector_register_finalizer((void *)suppressed_by_finalizer, count_finalization, NULL, NULL, NULL);
    collector_collect();
    assert(collector_invoke_finalizers() == 1 && counted == 3);
    assert(!collector_has_pending_finalizers());
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

static void weak_and_ephemeron_registrations_are_per_slot(void)
{
    frame_root = (uintptr_t)allocate_atomic(8);
    uintptr_t other = (uintptr_t)allocate_atomic(8);
    uintptr_t slot = 0;
    assert(collector_register_short_weak_reference(&slot, other));
    assert(collector_register_short_weak_reference(&slot, frame_root));
    assert(slot == frame_root);
    assert(collector_register_weak_reference(&slot, frame_root, NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION));
    assert(collector_unregister_weak_reference(&slot, NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 1);
    assert(collector_unregister_weak_reference(&slot, NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 0);
    assert(collector_unregister_weak_reference(&slot, NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 1);

    uintptr_t key = frame_root;
    uintptr_t value = other;
    uintptr_t elsewhere = 0;
    assert(collector_register_ephemeron(&key, &value));
    assert(collector_register_ephemeron(&key, &value));
    collector_unregister_ephemeron(&key, &elsewhere);
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, other));
    collector_unregister_ephemeron(&key, &value);
    frame_root = 0;
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

/* Registering, suppressing, releasing and finalizing cost the same however many entries are
 * alive; lists searched from their head made each of them grow with the live count. */
static void registry_costs_do_not_grow_with_their_entries(void)
{
    enum { COUNT = 50000 };
    uintptr_t *objects = calloc(COUNT, sizeof(uintptr_t));
    uintptr_t *weak = calloc(COUNT, sizeof(uintptr_t));
    uintptr_t *keys = calloc(COUNT, sizeof(uintptr_t));
    uintptr_t *values = calloc(COUNT, sizeof(uintptr_t));
    assert(objects != NULL && weak != NULL && keys != NULL && values != NULL);
    unrecorded_objects_are_atomic = true;
    collector_register_root_range(objects, objects + COUNT);
    for (size_t index = 0; index < COUNT; index++)
        objects[index] = (uintptr_t)collector_allocate_object(8, false);
    counted = 0;
    clock_t start = clock();
    for (size_t index = 0; index < COUNT; index++) {
        collector_register_finalizer((void *)objects[index], count_finalization, NULL, NULL, NULL);
        assert(collector_register_short_weak_reference(&weak[index], objects[index]));
        keys[index] = objects[index];
        values[index] = objects[(index + 1) % COUNT];
        assert(collector_register_ephemeron(&keys[index], &values[index]));
    }
    /* Newest first, the far end of a list kept in registration order. */
    for (size_t index = COUNT; index-- > 0;) {
        if (index % 2 == 0)
            collector_register_finalizer((void *)objects[index], NULL, NULL, NULL, NULL);
        assert(collector_unregister_weak_reference(&weak[index], NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION));
        collector_unregister_ephemeron(&keys[index], &values[index]);
    }
    collector_unregister_root_range(objects, objects + COUNT);
    collector_collect();
    assert(collector_invoke_finalizers() == COUNT / 2 && counted == COUNT / 2);
    assert(clock() - start < CLOCKS_PER_SEC);
    assert(!collector_has_pending_finalizers());
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
    unrecorded_objects_are_atomic = false;
    free(objects);
    free(weak);
    free(keys);
    free(values);
}

static void stores_hash_and_metrics(void)
{
    uintptr_t source[3] = {1, 2, 3};
    uintptr_t destination[3] = {0};
    collector_move_value_range(0, destination, source, 3, sizeof(uintptr_t));
    assert(memcmp(source, destination, sizeof(source)) == 0);
    collector_store_reference(0, &destination[1], 7);
    assert(destination[1] == 7);
    collector_clear_value_range(0, destination, 3, sizeof(uintptr_t));
    assert(destination[0] == 0 && destination[1] == 0 && destination[2] == 0);
    assert(collector_identity_hash(1234) == collector_identity_hash(1234));
    assert(collector_pinned_reference_count() == 0);
    collector_pin_reference(&destination[0]);
    assert(collector_pinned_reference_count() == 1);
    collector_unpin_reference(&destination[0]);
    assert(collector_pinned_reference_count() == 0);
    assert(collector_total_allocated_bytes() != 0);
    assert(collector_collection_count() != 0);
    uint64_t pause = 123;
    assert(!collector_try_read_total_pause_milliseconds(&pause) && pause == 123);
}

static void atomic_allocations_are_zeroed_after_reclamation(void)
{
    static const size_t sizes[] = {0, 1, 32, 4096, 8192};
    frame_root = 0;
    for (size_t iteration = 0; iteration < 3; iteration++) {
        for (size_t index = 0; index < sizeof(sizes) / sizeof(sizes[0]); index++) {
            size_t size = sizes[index];
            unsigned char *object = allocate_atomic(size);
            assert(object != NULL);
            for (size_t offset = 0; offset < size; offset++) assert(object[offset] == 0);
            memset(object, 0xa5, size);
            collector_collect();
            assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
        }
    }
}

int main(void)
{
    collector_initialize();
    allocation_and_roots();
    layouts_and_ranges();
    lifetime_contract();
    suppressed_queued_finalizers_are_no_longer_pending();
    finalizers_reregister_and_suppress_while_running();
    weak_and_ephemeron_registrations_are_per_slot();
    registry_costs_do_not_grow_with_their_entries();
    stores_hash_and_metrics();
    atomic_allocations_are_zeroed_after_reclamation();
    collector_clear_roots();
    netwasm_gc_destroy(netwasm_gc_runtime);
    return 0;
}
