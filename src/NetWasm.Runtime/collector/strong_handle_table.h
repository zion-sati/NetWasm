#ifndef NETWASM_STRONG_HANDLE_TABLE_H
#define NETWASM_STRONG_HANDLE_TABLE_H

#include <stdint.h>

typedef struct NetWasmStrongHandleTable {
    uintptr_t *slots;
    uint8_t *occupied;
    uint8_t *pinned;
    uint32_t capacity;
} NetWasmStrongHandleTable;

uint32_t strong_handle_table_create(NetWasmStrongHandleTable *table, uintptr_t target);
uint32_t strong_handle_table_create_pinned(NetWasmStrongHandleTable *table, uintptr_t target);
uintptr_t strong_handle_table_get(const NetWasmStrongHandleTable *table, uint32_t handle);
void strong_handle_table_set(NetWasmStrongHandleTable *table, uint32_t handle, uintptr_t target);
void strong_handle_table_release(NetWasmStrongHandleTable *table, uint32_t handle);
uint32_t strong_handle_table_count(const NetWasmStrongHandleTable *table);

#endif
