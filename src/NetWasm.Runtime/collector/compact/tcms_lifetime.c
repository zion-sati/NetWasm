/* SPDX-License-Identifier: Apache-2.0. NetWasm lifetime extensions.
 * Mark-completion Strategy and finalizer-drain Command. Registrations are
 * passive records; allocator callbacks are injected by the ported core.
 * No metadata allocation invokes collection. No record discovers strong roots
 * from arbitrary state, weak targets, ephemeron keys or client-data integers. */
#include "tcms_lifetime.h"

typedef struct FinalizerRecord {
    struct FinalizerRecord *next;
    uintptr_t object;
    TcmsFinalizer function;
    void *state;
    bool queued, running, batch;
} FinalizerRecord;

typedef struct WeakRecord {
    struct WeakRecord *next;
    uintptr_t *slot, target;
    TcmsWeakLifetime lifetime;
} WeakRecord;

typedef struct EphemeronRecord {
    struct EphemeronRecord *next;
    uintptr_t *key, *value;
} EphemeronRecord;

typedef struct {
    FinalizerRecord *finalizers;
    WeakRecord *weak;
    EphemeronRecord *ephemerons;
    bool invoking;
} Lifetime;

static void *create_record(Tcms *gc, size_t size)
{
    void *record = gc->allocate(size, _Alignof(max_align_t));
    if (record != NULL) {
        unsigned char *bytes = record;
        for (size_t index = 0; index < size; index++) bytes[index] = 0;
    }
    return record;
}

static void release_record(Tcms *gc, void *record, size_t size)
{
    gc->release(record, size, _Alignof(max_align_t));
}

static bool available_slot(Tcms *gc, const uintptr_t *slot)
{
    uintptr_t owner = tcms_base(gc, (uintptr_t)slot);
    return owner == 0 || tcms_is_marked(gc, owner);
}

static void mark_ephemerons(Tcms *gc, Lifetime *lifetime)
{
    bool changed;
    do {
        changed = false;
        for (EphemeronRecord *entry = lifetime->ephemerons; entry != NULL; entry = entry->next) {
            if (available_slot(gc, entry->key) && available_slot(gc, entry->value) &&
                *entry->key != 0 && tcms_is_marked(gc, *entry->key))
                changed = tcms_mark(gc, *entry->value) || changed;
        }
        tcms_drain_mark(gc);
    } while (changed);
}

static void clear_weak(Tcms *gc, Lifetime *lifetime, TcmsWeakLifetime phase)
{
    WeakRecord **link = &lifetime->weak;
    while (*link != NULL) {
        WeakRecord *entry = *link;
        bool available = available_slot(gc, entry->slot);
        /* All managed slot allocations still exist during the short phase.
         * Their owners may be promoted by finalization afterwards, so do not
         * discard registrations merely because an owner is initially white. */
        bool dead_owner = phase == TCMS_WEAK_LONG && !available;
        if (dead_owner || (entry->lifetime == phase && !tcms_is_marked(gc, entry->target))) {
            if (!dead_owner) *entry->slot = 0;
            *link = entry->next;
            release_record(gc, entry, sizeof(*entry));
        } else link = &entry->next;
    }
}

static void complete_mark(Tcms *gc)
{
    Lifetime *lifetime = gc->extension;
    /* Pending and currently executing callbacks are strong roots, including
     * their complete graphs across GC initiated inside another callback. */
    for (FinalizerRecord *entry = lifetime->finalizers; entry != NULL; entry = entry->next)
        if (entry->queued || entry->running) tcms_mark(gc, entry->object);
    tcms_drain_mark(gc);
    mark_ephemerons(gc, lifetime);
    clear_weak(gc, lifetime, TCMS_WEAK_SHORT);

    /* Snapshot the whole eligible set before promoting any finalizable graph. */
    for (FinalizerRecord *entry = lifetime->finalizers; entry != NULL; entry = entry->next)
        if (!entry->queued && !entry->running && entry->function != NULL &&
            !tcms_is_marked(gc, entry->object)) entry->queued = true;
    for (FinalizerRecord *entry = lifetime->finalizers; entry != NULL; entry = entry->next)
        if (entry->queued) tcms_mark(gc, entry->object);
    tcms_drain_mark(gc);
    /* Dependent values of finalization-promoted keys must survive too. */
    mark_ephemerons(gc, lifetime);
    clear_weak(gc, lifetime, TCMS_WEAK_LONG);

    EphemeronRecord **link = &lifetime->ephemerons;
    while (*link != NULL) {
        EphemeronRecord *entry = *link;
        bool available = available_slot(gc, entry->key) && available_slot(gc, entry->value);
        if (!available || !tcms_is_marked(gc, *entry->key)) {
            if (available) *entry->key = *entry->value = 0;
            *link = entry->next;
            release_record(gc, entry, sizeof(*entry));
        } else link = &entry->next;
    }
}

static void dispose(Tcms *gc)
{
    Lifetime *lifetime = gc->extension;
    while (lifetime->finalizers != NULL) {
        FinalizerRecord *entry = lifetime->finalizers;
        lifetime->finalizers = entry->next;
        release_record(gc, entry, sizeof(*entry));
    }
    while (lifetime->weak != NULL) {
        WeakRecord *entry = lifetime->weak;
        lifetime->weak = entry->next;
        release_record(gc, entry, sizeof(*entry));
    }
    while (lifetime->ephemerons != NULL) {
        EphemeronRecord *entry = lifetime->ephemerons;
        lifetime->ephemerons = entry->next;
        release_record(gc, entry, sizeof(*entry));
    }
    release_record(gc, lifetime, sizeof(*lifetime));
    gc->hooks = NULL;
    gc->extension = NULL;
}

static Lifetime *ensure_lifetime(Tcms *gc)
{
    static const TcmsHooks hooks = {complete_mark, dispose};
    if (gc->extension == NULL) {
        Lifetime *lifetime = create_record(gc, sizeof(*lifetime));
        if (lifetime == NULL) return NULL;
        gc->extension = lifetime;
        gc->hooks = &hooks;
    }
    return gc->extension;
}

TcmsResult tcms_register_finalizer(Tcms *gc, uintptr_t object, TcmsFinalizer function,
    void *state, TcmsFinalizer *previous, void **previous_state)
{
    if (object == 0 || tcms_base(gc, object) != object) return TCMS_UNKNOWN_OBJECT;
    Lifetime *lifetime = function == NULL ? gc->extension : ensure_lifetime(gc);
    if (function != NULL && lifetime == NULL) return TCMS_OUT_OF_MEMORY;
    FinalizerRecord **link = lifetime == NULL ? NULL : &lifetime->finalizers;
    while (link != NULL && *link != NULL && (*link)->object != object) link = &(*link)->next;
    FinalizerRecord *entry = link == NULL ? NULL : *link;
    if (entry == NULL && function != NULL) {
        entry = create_record(gc, sizeof(*entry));
        if (entry == NULL) return TCMS_OUT_OF_MEMORY;
        entry->object = object;
        *link = entry;
    }
    if (previous != NULL) *previous = entry == NULL ? NULL : entry->function;
    if (previous_state != NULL) *previous_state = entry == NULL ? NULL : entry->state;
    if (entry != NULL) {
        entry->function = function;
        entry->state = state;
        if (function == NULL && !entry->running) {
            *link = entry->next;
            release_record(gc, entry, sizeof(*entry));
        }
    }
    return TCMS_OK;
}

bool tcms_has_pending_finalizers(const Tcms *gc)
{
    const Lifetime *lifetime = gc->extension;
    if (lifetime == NULL) return false;
    for (FinalizerRecord *entry = lifetime->finalizers; entry != NULL; entry = entry->next)
        if (entry->queued) return true;
    return false;
}

size_t tcms_invoke_finalizers(Tcms *gc)
{
    Lifetime *lifetime = gc->extension;
    if (lifetime == NULL || lifetime->invoking || gc->collecting) return 0;
    lifetime->invoking = true;
    for (FinalizerRecord *entry = lifetime->finalizers; entry != NULL; entry = entry->next)
        entry->batch = entry->queued;
    size_t count = 0;
    for (;;) {
        FinalizerRecord *entry = lifetime->finalizers;
        while (entry != NULL && !entry->batch) entry = entry->next;
        if (entry == NULL) break;
        TcmsFinalizer function = entry->function;
        void *state = entry->state;
        entry->batch = entry->queued = false;
        entry->running = true;
        /* Consume before call: ReRegister inside callback creates the next
         * registration, while resurrection alone does not. */
        entry->function = NULL;
        entry->state = NULL;
        function((void *)entry->object, state);
        count++;
        entry->running = false;
        if (entry->function == NULL) {
            FinalizerRecord **link = &lifetime->finalizers;
            while (*link != entry) link = &(*link)->next;
            *link = entry->next;
            release_record(gc, entry, sizeof(*entry));
        }
    }
    lifetime->invoking = false;
    return count;
}

TcmsResult tcms_register_weak(Tcms *gc, uintptr_t *slot, uintptr_t target, TcmsWeakLifetime phase)
{
    if (slot == NULL || (phase != TCMS_WEAK_SHORT && phase != TCMS_WEAK_LONG)) return TCMS_INVALID_ARGUMENT;
    if (target == 0) return TCMS_OK;
    if (tcms_base(gc, target) != target) return TCMS_UNKNOWN_OBJECT;
    Lifetime *lifetime = ensure_lifetime(gc);
    if (lifetime == NULL) return TCMS_OUT_OF_MEMORY;
    WeakRecord *entry = lifetime->weak;
    while (entry != NULL && (entry->slot != slot || entry->lifetime != phase)) entry = entry->next;
    if (entry == NULL) {
        entry = create_record(gc, sizeof(*entry));
        if (entry == NULL) return TCMS_OUT_OF_MEMORY;
        entry->next = lifetime->weak;
        lifetime->weak = entry;
    }
    *slot = target;
    entry->slot = slot;
    entry->target = target;
    entry->lifetime = phase;
    return TCMS_OK;
}

bool tcms_unregister_weak(Tcms *gc, uintptr_t *slot, TcmsWeakLifetime phase)
{
    Lifetime *lifetime = gc->extension;
    if (lifetime == NULL) return false;
    WeakRecord **link = &lifetime->weak;
    while (*link != NULL && ((*link)->slot != slot || (*link)->lifetime != phase)) link = &(*link)->next;
    if (*link == NULL) return false;
    WeakRecord *entry = *link;
    *link = entry->next;
    release_record(gc, entry, sizeof(*entry));
    return true;
}

TcmsResult tcms_register_ephemeron(Tcms *gc, uintptr_t *key, uintptr_t *value)
{
    if (key == NULL || value == NULL || *key == 0 || tcms_base(gc, *key) != *key)
        return TCMS_INVALID_ARGUMENT;
    Lifetime *lifetime = ensure_lifetime(gc);
    if (lifetime == NULL) return TCMS_OUT_OF_MEMORY;
    for (EphemeronRecord *entry = lifetime->ephemerons; entry != NULL; entry = entry->next)
        if (entry->key == key && entry->value == value) return TCMS_OK;
    EphemeronRecord *entry = create_record(gc, sizeof(*entry));
    if (entry == NULL) return TCMS_OUT_OF_MEMORY;
    *entry = (EphemeronRecord){lifetime->ephemerons, key, value};
    lifetime->ephemerons = entry;
    return TCMS_OK;
}

bool tcms_unregister_ephemeron(Tcms *gc, uintptr_t *key, uintptr_t *value)
{
    Lifetime *lifetime = gc->extension;
    if (lifetime == NULL) return false;
    EphemeronRecord **link = &lifetime->ephemerons;
    while (*link != NULL && ((*link)->key != key || (*link)->value != value)) link = &(*link)->next;
    if (*link == NULL) return false;
    EphemeronRecord *entry = *link;
    *link = entry->next;
    release_record(gc, entry, sizeof(*entry));
    return true;
}
