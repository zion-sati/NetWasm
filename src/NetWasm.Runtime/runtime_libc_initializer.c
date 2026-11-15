// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include "runtime_libc_initializer.h"

extern void __wasm_call_ctors(void);
static int initialized;

void runtime_initialize_libc(void)
{
    if (initialized) {
        return;
    }
    __wasm_call_ctors();
    initialized = 1;
}
