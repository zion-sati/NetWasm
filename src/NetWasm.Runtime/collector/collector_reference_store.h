// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_COLLECTOR_REFERENCE_STORE_H
#define NETWASM_COLLECTOR_REFERENCE_STORE_H

#include "../native_target.h"
#include <stddef.h>
#include <stdint.h>

/* A collector with generations is told of every reference stored into an object that may have survived a
 * collection: at the address stored through, or at the start of the object or value stored into. A range of
 * memory that received references is told as a whole. For every other collector these are nothing, and no
 * compiled program calls them. */
#ifdef NETWASM_GC_GENERATIONAL
#include "netwasm/reference_cards.h"
#define collector_record_reference_write(address) netwasm_gc_record_reference_write(address)
#define collector_record_reference_writes(first, bytes) netwasm_gc_record_reference_writes(first, bytes)
#else
#define collector_record_reference_write(address) ((void)(address))
#define collector_record_reference_writes(first, bytes) ((void)(first), (void)(bytes))
#endif

void collector_store_reference(
    netwasm_reference_t owner,
    netwasm_reference_t *slot,
    netwasm_reference_t value);

void collector_move_value_range(
    netwasm_reference_t owner,
    void *destination,
    const void *source,
    size_t element_count,
    size_t element_size);

void collector_clear_value_range(
    netwasm_reference_t owner,
    void *destination,
    size_t element_count,
    size_t element_size);

#endif
