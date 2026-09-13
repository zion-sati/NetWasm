#ifndef NETWASM_COLLECTOR_REFERENCE_STORE_H
#define NETWASM_COLLECTOR_REFERENCE_STORE_H

#include "../native_target.h"
#include "collector_allocation.h"
#include <stddef.h>

void collector_store_reference(
    netwasm_reference_t owner,
    netwasm_reference_t *slot,
    netwasm_reference_t value);

void collector_move_value_range(
    netwasm_reference_t owner,
    void *destination,
    const void *source,
    size_t element_count,
    size_t element_size,
    NetWasmCollectorDescriptor descriptor);

void collector_clear_value_range(
    netwasm_reference_t owner,
    void *destination,
    size_t element_count,
    size_t element_size,
    NetWasmCollectorDescriptor descriptor);

#endif
