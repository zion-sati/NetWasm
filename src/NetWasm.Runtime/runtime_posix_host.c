// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <errno.h>
#include <stddef.h>
#include <time.h>
#include <unistd.h>

#include "runtime_clock_reader.h"

/*
 * The C library's clock and entropy calls for native code, answered by the host through the platform's readers
 * (runtime_clock_reader.c, or runtime_platform_nowasi.c for a plain core module). musl's own versions import WASI
 * Preview 1. All are weak, as the C library's are, so a native library's own definitions win, and a program whose
 * native code calls none of them links none.
 */

__attribute__((weak)) int __clock_gettime(clockid_t clock, struct timespec *time)
{
    int error = runtime_read_clock(clock, time);
    if (error != 0) {
        errno = error;
        return -1;
    }
    return 0;
}

__attribute__((weak)) int clock_gettime(clockid_t clock, struct timespec *time)
{
    return __clock_gettime(clock, time);
}

__attribute__((weak)) int clock_getres(clockid_t clock, struct timespec *resolution)
{
    struct timespec ignored;
    int error = runtime_read_clock_resolution(clock, resolution == NULL ? &ignored : resolution);
    if (error != 0) {
        errno = error;
        return -1;
    }
    return 0;
}

/* As musl's: at most 256 bytes per call. */
__attribute__((weak)) int getentropy(void *buffer, size_t length)
{
    if (length > 256) {
        errno = EIO;
        return -1;
    }
    int error = runtime_read_entropy(buffer, length);
    if (error != 0) {
        errno = error;
        return -1;
    }
    return 0;
}
