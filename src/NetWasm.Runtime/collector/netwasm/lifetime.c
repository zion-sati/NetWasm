/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "lifetime.h"

#include "../collector_metadata.h"
#include "address_index.h"
#include "runtime_state.h"

#include <limits.h>
#include <stdlib.h>

/* Each registry keeps its entries on a circular list for the collector's passes and in an
 * address index for registration, so registering, suppressing or releasing one entry costs
 * the same however many the program keeps alive. */
typedef struct Link Link;
struct Link {
    Link *previous;
    Link *next;
};

typedef struct {
    Link link;
    uintptr_t object;
    NetWasmCollectorFinalizer finalizer;
    void *context;
    bool running;
} FinalizerEntry;

typedef struct {
    Link link;
    uintptr_t *slot;
    uintptr_t target;
    NetWasmWeakReferenceLifetime lifetime;
} WeakEntry;

typedef struct {
    Link link;
    uintptr_t *key;
    uintptr_t *value;
} EphemeronEntry;

typedef struct {
    NetWasmGc *gc;
    /* A finalizer entry is registered, queued after a collection found its object
     * unreachable, draining in the current invocation, or running. */
    Link registered;
    Link queued;
    Link draining;
    FinalizerEntry *running;
    NetWasmAddressIndex finalizers;
    Link weak;
    NetWasmAddressIndex weak_slots;
    Link ephemerons;
    NetWasmAddressIndex ephemeron_keys;
    bool invoking;
} LifetimeState;

static LifetimeState lifetime;

static void list_initialize(Link *list)
{
    list->previous = list;
    list->next = list;
}

static bool list_is_empty(const Link *list)
{
    return list->next == list;
}

static void list_append(Link *list, Link *link)
{
    link->previous = list->previous;
    link->next = list;
    list->previous->next = link;
    list->previous = link;
}

static void list_remove(Link *link)
{
    link->previous->next = link->next;
    link->next->previous = link->previous;
}

/* Moves every entry of from to the end of to, in order. */
static void list_splice(Link *to, Link *from)
{
    if (list_is_empty(from)) return;
    from->next->previous = to->previous;
    to->previous->next = from->next;
    from->previous->next = to;
    to->previous = from->previous;
    list_initialize(from);
}

/* A weak slot holds one registration per lifetime; slots are pointer-aligned, so the
 * lifetime fits the key's low bit. */
static uintptr_t weak_key(uintptr_t *slot, NetWasmWeakReferenceLifetime weak_lifetime)
{
    return (uintptr_t)slot | (uintptr_t)weak_lifetime;
}

static void release_finalizer(FinalizerEntry *entry)
{
    netwasm_address_index_remove(&lifetime.finalizers, entry->object);
    collector_release_metadata(entry);
}

static void release_weak(WeakEntry *entry)
{
    list_remove(&entry->link);
    netwasm_address_index_remove(&lifetime.weak_slots, weak_key(entry->slot, entry->lifetime));
    collector_release_metadata(entry);
}

static void release_ephemeron(EphemeronEntry *entry)
{
    list_remove(&entry->link);
    netwasm_address_index_remove(&lifetime.ephemeron_keys, (uintptr_t)entry->key);
    collector_release_metadata(entry);
}

static bool slot_is_available(uintptr_t *slot)
{
    uintptr_t owner = netwasm_gc_base(lifetime.gc, (uintptr_t)slot);
    return owner == 0 || netwasm_gc_is_marked(lifetime.gc, owner);
}

static void drain_ephemerons(void)
{
    bool changed;
    do {
        changed = false;
        for (Link *link = lifetime.ephemerons.next; link != &lifetime.ephemerons; link = link->next) {
            EphemeronEntry *entry = (EphemeronEntry *)link;
            if (!slot_is_available(entry->key) || !slot_is_available(entry->value)) continue;
            if (*entry->key != 0 && netwasm_gc_is_marked(lifetime.gc, *entry->key))
                changed = netwasm_gc_mark(lifetime.gc, *entry->value) || changed;
        }
        netwasm_gc_drain(lifetime.gc);
    } while (changed);
}

static void process_weak(NetWasmWeakReferenceLifetime phase)
{
    Link *next;
    for (Link *link = lifetime.weak.next; link != &lifetime.weak; link = next) {
        next = link->next;
        WeakEntry *entry = (WeakEntry *)link;
        bool owner_available = slot_is_available(entry->slot);
        bool discard_owner = phase == NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION && !owner_available;
        bool clear_target = entry->lifetime == phase &&
            !netwasm_gc_is_marked(lifetime.gc, entry->target);
        if (!discard_owner && !clear_target) continue;
        if (!discard_owner) *entry->slot = 0;
        release_weak(entry);
    }
}

static void mark_finalizers(NetWasmGc *gc, const Link *list)
{
    for (Link *link = list->next; link != list; link = link->next)
        netwasm_gc_mark(gc, ((FinalizerEntry *)link)->object);
}

static void finish_mark(NetWasmGc *gc, void *state)
{
    (void)state;
    mark_finalizers(gc, &lifetime.queued);
    mark_finalizers(gc, &lifetime.draining);
    if (lifetime.running != NULL) netwasm_gc_mark(gc, lifetime.running->object);
    netwasm_gc_drain(gc);
    drain_ephemerons();
    process_weak(NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);

    /* Registered entries always have a finalizer: suppression removes the rest. */
    Link *next;
    for (Link *link = lifetime.registered.next; link != &lifetime.registered; link = next) {
        next = link->next;
        FinalizerEntry *entry = (FinalizerEntry *)link;
        if (netwasm_gc_is_marked(gc, entry->object)) continue;
        list_remove(link);
        list_append(&lifetime.queued, link);
    }
    mark_finalizers(gc, &lifetime.queued);
    netwasm_gc_drain(gc);
    drain_ephemerons();
    process_weak(NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION);

    for (Link *link = lifetime.ephemerons.next; link != &lifetime.ephemerons; link = next) {
        next = link->next;
        EphemeronEntry *entry = (EphemeronEntry *)link;
        bool available = slot_is_available(entry->key) && slot_is_available(entry->value);
        if (available && netwasm_gc_is_marked(gc, *entry->key)) continue;
        if (available) {
            *entry->key = 0;
            *entry->value = 0;
        }
        release_ephemeron(entry);
    }
}

void netwasm_gc_lifetime_initialize(NetWasmGc *gc)
{
    if (lifetime.gc != NULL && lifetime.gc != gc) abort();
    if (lifetime.gc == NULL) {
        list_initialize(&lifetime.registered);
        list_initialize(&lifetime.queued);
        list_initialize(&lifetime.draining);
        list_initialize(&lifetime.weak);
        list_initialize(&lifetime.ephemerons);
    }
    lifetime.gc = gc;
    netwasm_gc_set_mark_completion(gc, finish_mark, NULL);
}

void netwasm_gc_register_finalizer(
    uintptr_t object,
    NetWasmCollectorFinalizer finalizer,
    void *context,
    NetWasmCollectorFinalizer *previous,
    void **previous_context)
{
    if (object == 0 || netwasm_gc_base(lifetime.gc, object) != object) abort();
    FinalizerEntry *entry = netwasm_address_index_find(&lifetime.finalizers, object);
    if (entry == NULL && finalizer != NULL) {
        entry = collector_allocate_metadata(sizeof(*entry));
        if (entry == NULL || !netwasm_address_index_insert(&lifetime.finalizers, object, entry))
            abort();
        entry->object = object;
        list_append(&lifetime.registered, &entry->link);
    }
    if (previous != NULL) *previous = entry == NULL ? NULL : entry->finalizer;
    if (previous_context != NULL) *previous_context = entry == NULL ? NULL : entry->context;
    if (entry == NULL) return;
    entry->finalizer = finalizer;
    entry->context = context;
    /* A finalizer may suppress an object queued beside its own; a running entry is released
     * when its finalizer returns. */
    if (finalizer == NULL && !entry->running) {
        list_remove(&entry->link);
        release_finalizer(entry);
    }
}

bool netwasm_gc_has_pending_finalizers(void)
{
    return !list_is_empty(&lifetime.queued);
}

int netwasm_gc_invoke_finalizers(void)
{
    if (lifetime.invoking || netwasm_gc_is_collecting(lifetime.gc)) return 0;
    lifetime.invoking = true;
    /* This invocation runs the entries queued now; a collection inside a finalizer queues
     * more for the next one. */
    list_splice(&lifetime.draining, &lifetime.queued);
    size_t invoked = 0;
    while (!list_is_empty(&lifetime.draining)) {
        FinalizerEntry *entry = (FinalizerEntry *)lifetime.draining.next;
        list_remove(&entry->link);
        NetWasmCollectorFinalizer callback = entry->finalizer;
        void *context = entry->context;
        entry->running = true;
        entry->finalizer = NULL;
        entry->context = NULL;
        lifetime.running = entry;
        if (callback != NULL) callback((void *)entry->object, context);
        lifetime.running = NULL;
        entry->running = false;
        if (invoked != SIZE_MAX) invoked++;
        /* ReRegisterForFinalize from its own finalizer registers the object again. */
        if (entry->finalizer != NULL) list_append(&lifetime.registered, &entry->link);
        else release_finalizer(entry);
    }
    lifetime.invoking = false;
    return invoked > INT_MAX ? INT_MAX : (int)invoked;
}

int netwasm_gc_register_weak(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime weak_lifetime)
{
    if (slot == NULL || weak_lifetime > NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) return 0;
    uintptr_t key = weak_key(slot, weak_lifetime);
    WeakEntry *entry = netwasm_address_index_find(&lifetime.weak_slots, key);
    if (target == 0) {
        if (entry != NULL) release_weak(entry);
        return 1;
    }
    if (netwasm_gc_base(lifetime.gc, target) == 0) return 0;
    if (entry == NULL) {
        entry = collector_allocate_metadata(sizeof(*entry));
        if (entry == NULL) return 0;
        if (!netwasm_address_index_insert(&lifetime.weak_slots, key, entry)) {
            collector_release_metadata(entry);
            return 0;
        }
        entry->slot = slot;
        entry->lifetime = weak_lifetime;
        list_append(&lifetime.weak, &entry->link);
    }
    entry->target = target;
    *slot = target;
    return 1;
}

int netwasm_gc_unregister_weak(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime weak_lifetime)
{
    WeakEntry *entry = netwasm_address_index_find(&lifetime.weak_slots, weak_key(slot, weak_lifetime));
    if (entry == NULL) return 0;
    release_weak(entry);
    return 1;
}

int netwasm_gc_register_ephemeron(uintptr_t *key, uintptr_t *value)
{
    if (key == NULL || value == NULL) return 0;
    EphemeronEntry *entry = netwasm_address_index_find(&lifetime.ephemeron_keys, (uintptr_t)key);
    if (entry != NULL) {
        /* A key slot pairs with one value slot. */
        if (entry->value != value) abort();
        return 1;
    }
    entry = collector_allocate_metadata(sizeof(*entry));
    if (entry == NULL) return 0;
    if (!netwasm_address_index_insert(&lifetime.ephemeron_keys, (uintptr_t)key, entry)) {
        collector_release_metadata(entry);
        return 0;
    }
    entry->key = key;
    entry->value = value;
    list_append(&lifetime.ephemerons, &entry->link);
    return 1;
}

void netwasm_gc_unregister_ephemeron(uintptr_t *key, uintptr_t *value)
{
    EphemeronEntry *entry = netwasm_address_index_find(&lifetime.ephemeron_keys, (uintptr_t)key);
    if (entry == NULL || entry->value != value) return;
    release_ephemeron(entry);
}
