// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_RUNTIME_CLOCK_READER_H
#define NETWASM_RUNTIME_CLOCK_READER_H

#include <stddef.h>
#include <time.h>

/* The host's clocks and entropy for native code's C library: 0, or an errno value. The WASI
 * platform reads wasi:clocks and wasi:random; a plain core module has neither (ENOSYS). */
int runtime_read_clock(clockid_t clock, struct timespec *time);
int runtime_read_clock_resolution(clockid_t clock, struct timespec *resolution);
int runtime_read_entropy(void *buffer, size_t length);

#endif
