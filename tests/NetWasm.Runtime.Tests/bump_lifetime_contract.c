// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <assert.h>
#include <stddef.h>
#include <stdint.h>

#include "collector/bump/bump_arena.h"
#include "collector/collector_ephemeron.h"
#include "collector/collector_finalization.h"
#include "collector/collector_weak_reference.h"

static int bump_allocate_calls;

void runtime_initialize_libc(void) { }
void netwasm_bump_initialize(void) { }
void *netwasm_bump_allocate(size_t size)
{
    (void)size;
    bump_allocate_calls++;
    return NULL;
}
uint64_t netwasm_bump_total_allocated_bytes(void) { return 0; }
uint64_t netwasm_bump_heap_size(void) { return 0; }
uint64_t netwasm_bump_free_bytes(void) { return 0; }

static int finalizer_calls;

static void count_finalizer(void *object, void *context)
{
    (void)object;
    (void)context;
    finalizer_calls++;
}

/* Bump never reclaims an object, so registering a finalizer or an ephemeron succeeds and nothing
 * ever becomes due or clears. */
static void bump_accepts_finalizers_and_ephemerons(void)
{
    int object = 0;
    NetWasmCollectorFinalizer previous = count_finalizer;
    void *previous_context = &object;
    collector_register_finalizer(&object, count_finalizer, &object, &previous, &previous_context);
    assert(previous == NULL && previous_context == NULL);
    collector_register_finalizer(&object, count_finalizer, NULL, NULL, NULL);
    assert(!collector_has_pending_finalizers());
    assert(collector_invoke_finalizers() == 0);
    assert(finalizer_calls == 0);

    uintptr_t key = 0x1234;
    uintptr_t value = 0x5678;
    assert(collector_register_ephemeron(&key, &value) == 1);
    collector_unregister_ephemeron(&key, &value);
    assert(key == 0x1234 && value == 0x5678);
}

int main(void)
{
    uintptr_t slot = 0x1234;

    assert(collector_register_weak_reference(
        &slot,
        slot,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 1);
    assert(slot == 0x1234);
    assert(collector_register_weak_reference(
        &slot,
        slot,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 1);
    assert(slot == 0x1234);
    assert(collector_register_short_weak_reference(&slot, slot) == 1);
    assert(slot == 0x1234);
    assert(collector_register_short_weak_reference(&slot, slot) == 1);
    assert(slot == 0x1234);
    assert(collector_register_short_weak_reference(&slot, 0) == 1);
    assert(slot == 0x1234);
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 0);
    assert(slot == 0x1234);
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 0);
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 0);
    assert(slot == 0x1234);
    bump_accepts_finalizers_and_ephemerons();
    assert(bump_allocate_calls == 0);

    return 0;
}
