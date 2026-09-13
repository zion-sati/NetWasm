#ifndef NETWASM_GC_HANDLE_TABLE_H
#define NETWASM_GC_HANDLE_TABLE_H

#include "strong_handle_table.h"
#include "weak_handle_table.h"

typedef struct NetWasmGcHandleTable {
    NetWasmWeakHandleTable weak;
    NetWasmStrongHandleTable strong;
} NetWasmGcHandleTable;

uint32_t gc_handle_table_create(NetWasmGcHandleTable *table, uintptr_t target, uint32_t kind);
uintptr_t gc_handle_table_get(const NetWasmGcHandleTable *table, uint32_t handle);
void gc_handle_table_set(NetWasmGcHandleTable *table, uint32_t handle, uintptr_t target);
void gc_handle_table_release(NetWasmGcHandleTable *table, uint32_t handle);

#endif
