#ifndef NETWASM_WEAK_HANDLE_TABLE_H
#define NETWASM_WEAK_HANDLE_TABLE_H

#include <stdbool.h>
#include <stdint.h>

typedef struct NetWasmWeakHandleTable {
    uintptr_t *slots;
    uint8_t *lifetimes;
    uint8_t *occupied;
    uint32_t capacity;
} NetWasmWeakHandleTable;

uint32_t weak_handle_table_create(
    NetWasmWeakHandleTable *table,
    uintptr_t target,
    bool track_resurrection);
uintptr_t weak_handle_table_get(const NetWasmWeakHandleTable *table, uint32_t handle);
void weak_handle_table_set(NetWasmWeakHandleTable *table, uint32_t handle, uintptr_t target);
void weak_handle_table_release(NetWasmWeakHandleTable *table, uint32_t handle);

#endif
