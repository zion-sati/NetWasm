#ifndef NETWASM_EPHEMERON_HANDLE_TABLE_H
#define NETWASM_EPHEMERON_HANDLE_TABLE_H

#include <stdint.h>

typedef struct NetWasmEphemeronHandleTable {
    uintptr_t *keys;
    uintptr_t *values;
    uint8_t *occupied;
    uint32_t capacity;
} NetWasmEphemeronHandleTable;

uint32_t ephemeron_handle_table_create(
    NetWasmEphemeronHandleTable *table,
    uintptr_t key,
    uintptr_t value);
uintptr_t ephemeron_handle_table_get_key(
    const NetWasmEphemeronHandleTable *table,
    uint32_t handle);
uintptr_t ephemeron_handle_table_get_value(
    const NetWasmEphemeronHandleTable *table,
    uint32_t handle);
void ephemeron_handle_table_release(
    NetWasmEphemeronHandleTable *table,
    uint32_t handle);

#endif
