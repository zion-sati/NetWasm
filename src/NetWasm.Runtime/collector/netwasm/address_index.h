/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GC_ADDRESS_INDEX_H
#define NETWASM_GC_ADDRESS_INDEX_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/* Maps a nonzero address to an entry, so a registry finds, adds and removes an entry in
 * constant expected time however many it holds. Storage comes from collector metadata. */
typedef struct {
    uintptr_t *keys;
    void **values;
    size_t capacity;
    size_t count;
} NetWasmAddressIndex;

void *netwasm_address_index_find(const NetWasmAddressIndex *index, uintptr_t key);
/* The key must be absent. Fails, leaving the index unchanged, when storage is unavailable. */
bool netwasm_address_index_insert(NetWasmAddressIndex *index, uintptr_t key, void *value);
void netwasm_address_index_remove(NetWasmAddressIndex *index, uintptr_t key);

#endif
