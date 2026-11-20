// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_WASI_CLOCKS_IMPORT_H
#define NETWASM_WASI_CLOCKS_IMPORT_H

#include <stdint.h>
#include "wasi_import_attributes.h"

/* wasi:clocks/system-clock's instant, as the canonical ABI lays the record out. */
typedef struct runtime_system_clock_instant {
    int64_t seconds;
    uint32_t nanoseconds;
} runtime_system_clock_instant;

NETWASM_WASI_IMPORT("wasi:clocks/system-clock@0.3", "now")
void runtime_system_clock_now(runtime_system_clock_instant *result);

NETWASM_WASI_IMPORT("wasi:clocks/system-clock@0.3", "get-resolution")
uint64_t runtime_system_clock_resolution(void);

NETWASM_WASI_IMPORT("wasi:clocks/monotonic-clock@0.3", "now")
uint64_t runtime_monotonic_clock_now(void);

NETWASM_WASI_IMPORT("wasi:clocks/monotonic-clock@0.3", "get-resolution")
uint64_t runtime_monotonic_clock_resolution(void);

#endif
