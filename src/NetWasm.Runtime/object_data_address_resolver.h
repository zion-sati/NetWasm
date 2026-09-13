#ifndef NETWASM_OBJECT_DATA_ADDRESS_RESOLVER_H
#define NETWASM_OBJECT_DATA_ADDRESS_RESOLVER_H

#include "native_target.h"

#include <stdint.h>

enum NetWasmObjectDataKind {
    NETWASM_OBJECT_DATA_FIELDS = 0,
    NETWASM_OBJECT_DATA_STRING = 1,
    NETWASM_OBJECT_DATA_ARRAY = 2,
};

netwasm_address_t object_data_address_resolve(
    netwasm_reference_t target,
    uint32_t type_capacity,
    const uint8_t *type_data_kinds,
    netwasm_address_t object_header_size,
    netwasm_address_t string_data_offset,
    netwasm_address_t array_data_pointer_offset);

#endif
