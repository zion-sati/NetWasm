#ifndef NETWASM_COLLECTOR_WEAK_REFERENCE_H
#define NETWASM_COLLECTOR_WEAK_REFERENCE_H

#include <stdint.h>

typedef enum NetWasmWeakReferenceLifetime {
    NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION = 0,
    NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION = 1,
} NetWasmWeakReferenceLifetime;

int collector_register_weak_reference(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime lifetime);
int collector_register_short_weak_reference(uintptr_t *slot, uintptr_t target);

int collector_unregister_weak_reference(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime lifetime);

#endif
