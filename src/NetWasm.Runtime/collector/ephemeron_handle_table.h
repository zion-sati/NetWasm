// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_EPHEMERON_HANDLE_TABLE_H
#define NETWASM_EPHEMERON_HANDLE_TABLE_H

#include <stdint.h>

typedef struct NetWasmEphemeronHandleTable {
    uintptr_t *keys;
    uintptr_t *values;
    uint32_t *next_free;
    uint8_t *occupied;
    uint32_t capacity;
    uint32_t next_index;
    uint32_t free_head;
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
void ephemeron_handle_table_set_target_to_null(
    NetWasmEphemeronHandleTable *table,
    uint32_t handle);
void ephemeron_handle_table_set_value(
    NetWasmEphemeronHandleTable *table,
    uint32_t handle,
    uintptr_t value);
void ephemeron_handle_table_release(
    NetWasmEphemeronHandleTable *table,
    uint32_t handle);

#endif
