#include "collector_weak_reference.h"

#include <gc.h>

int collector_register_weak_reference(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime lifetime)
{
    if (target == 0) {
        return 1;
    }

    int result;
    if (lifetime == NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) {
        result = GC_register_long_link((void **)slot, (const void *)target);
    } else {
        result = GC_general_register_disappearing_link((void **)slot, (const void *)target);
    }

    return result == GC_SUCCESS || result == GC_DUPLICATE;
}

int collector_register_short_weak_reference(uintptr_t *slot, uintptr_t target)
{
    return collector_register_weak_reference(
        slot,
        target,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);
}

int collector_unregister_weak_reference(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime lifetime)
{
    return lifetime == NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION
        ? GC_unregister_long_link((void **)slot)
        : GC_unregister_disappearing_link((void **)slot);
}
