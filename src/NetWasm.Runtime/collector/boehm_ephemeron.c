#include "collector_ephemeron.h"

#include "collector_metadata.h"
#include "collector_weak_reference.h"

#include <gc.h>
/* BDWGC 8.2.8, pinned by eng/toolchain.json, has no public ephemeron API.
   NetWasm therefore completes ephemeron marking at GC_EVENT_MARK_END: a value
   is marked only when its weak key was reached independently, and the normal
   mark stack is drained until chained ephemerons reach a fixed point. The
   callback runs before BDWGC clears short disappearing links in GC_finalize.

   MARK_FROM_MARK_STACK is intentionally the only private collector surface
   used here. Any BDWGC upgrade must revalidate this mark-end ordering and the
   private macro contract with the ephemeron collector regression before the
   pinned version changes. */
#include <private/gc_pmark.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>

typedef struct NetWasmEphemeronRegistry {
    uintptr_t **key_slots;
    uintptr_t **value_slots;
    uint8_t *occupied;
    uint32_t capacity;
} NetWasmEphemeronRegistry;

static NetWasmEphemeronRegistry registry;
static GC_on_collection_event_proc previous_collection_event;
static bool collection_event_installed;

static void drain_mark_stack(void)
{
    while (!GC_mark_stack_empty()) MARK_FROM_MARK_STACK();
}

static void mark_live_ephemeron_values(void)
{
    bool changed;
    do {
        changed = false;
        for (uint32_t index = 0; index < registry.capacity; index++) {
            if (registry.occupied[index] == 0) continue;

            uintptr_t key = *registry.key_slots[index];
            uintptr_t value = *registry.value_slots[index];
            if (key == 0 || value == 0 || !GC_is_marked((void *)key) ||
                GC_is_marked((void *)value)) {
                continue;
            }

            GC_push_all_eager(
                registry.value_slots[index],
                registry.value_slots[index] + 1);
            drain_mark_stack();
            changed = GC_is_marked((void *)value) || changed;
        }
    } while (changed);
}

static void on_collection_event(GC_EventType event)
{
    if (previous_collection_event != NULL) previous_collection_event(event);
    if (event == GC_EVENT_MARK_END) mark_live_ephemeron_values();
}

static void install_collection_event(void)
{
    if (collection_event_installed) return;
    previous_collection_event = GC_get_on_collection_event();
    GC_set_on_collection_event(on_collection_event);
    collection_event_installed = true;
}

static void grow(void)
{
    uint32_t capacity = registry.capacity == 0 ? 1 : registry.capacity * 2;
    if (capacity <= registry.capacity) abort();

    uintptr_t **key_slots = collector_allocate_metadata(capacity * sizeof(uintptr_t *));
    uintptr_t **value_slots = collector_allocate_metadata(capacity * sizeof(uintptr_t *));
    uint8_t *occupied = collector_allocate_metadata(capacity * sizeof(uint8_t));
    if (key_slots == NULL || value_slots == NULL || occupied == NULL) abort();

    memset(key_slots, 0, capacity * sizeof(uintptr_t *));
    memset(value_slots, 0, capacity * sizeof(uintptr_t *));
    memset(occupied, 0, capacity * sizeof(uint8_t));
    if (registry.capacity != 0) {
        memcpy(key_slots, registry.key_slots, registry.capacity * sizeof(uintptr_t *));
        memcpy(value_slots, registry.value_slots, registry.capacity * sizeof(uintptr_t *));
        memcpy(occupied, registry.occupied, registry.capacity * sizeof(uint8_t));
    }

    collector_release_metadata(registry.key_slots);
    collector_release_metadata(registry.value_slots);
    collector_release_metadata(registry.occupied);
    registry.key_slots = key_slots;
    registry.value_slots = value_slots;
    registry.occupied = occupied;
    registry.capacity = capacity;
}

int collector_register_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    if (key_slot == NULL || value_slot == NULL || *key_slot == 0) return 0;

    uint32_t index = 0;
    while (index < registry.capacity && registry.occupied[index] != 0) index++;
    if (index == registry.capacity) grow();

    if (!collector_register_short_weak_reference(key_slot, *key_slot)) return 0;
    registry.key_slots[index] = key_slot;
    registry.value_slots[index] = value_slot;
    registry.occupied[index] = 1;
    install_collection_event();
    return 1;
}

void collector_unregister_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    if (key_slot == NULL || value_slot == NULL) return;

    for (uint32_t index = 0; index < registry.capacity; index++) {
        if (registry.occupied[index] == 0 || registry.key_slots[index] != key_slot ||
            registry.value_slots[index] != value_slot) {
            continue;
        }

        if (*key_slot != 0) {
            collector_unregister_weak_reference(
                key_slot,
                NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);
        }
        registry.key_slots[index] = NULL;
        registry.value_slots[index] = NULL;
        registry.occupied[index] = 0;
        return;
    }
}
