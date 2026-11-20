// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <errno.h>
#include <stdint.h>
#include <string.h>
#include <time.h>

#include "runtime_clock_reader.h"
#include "wasi_clocks_import.h"
#include "wasi_random_import.h"

/* Native code's clocks and entropy, from wasi:clocks and wasi:random. The system clock answers
 * the real-time clocks and the monotonic clock the rest; WASI has no CPU-time clock, so the
 * process and thread CPU clocks read the monotonic clock, as WASI's own C library emulates them.
 * A program whose native code reads no clock leaves these, and their imports, out. */

enum clock_source { CLOCK_SOURCE_NONE, CLOCK_SOURCE_SYSTEM, CLOCK_SOURCE_MONOTONIC };

static enum clock_source select_clock(clockid_t clock)
{
    switch (clock) {
    case CLOCK_REALTIME:
    case CLOCK_REALTIME_COARSE:
        return CLOCK_SOURCE_SYSTEM;
    case CLOCK_MONOTONIC:
    case CLOCK_MONOTONIC_RAW:
    case CLOCK_MONOTONIC_COARSE:
    case CLOCK_BOOTTIME:
    case CLOCK_PROCESS_CPUTIME_ID:
    case CLOCK_THREAD_CPUTIME_ID:
        return CLOCK_SOURCE_MONOTONIC;
    default:
        return CLOCK_SOURCE_NONE;
    }
}

static void from_nanoseconds(uint64_t nanoseconds, struct timespec *time)
{
    time->tv_sec = (time_t)(nanoseconds / 1000000000u);
    time->tv_nsec = (long)(nanoseconds % 1000000000u);
}

int runtime_read_clock(clockid_t clock, struct timespec *time)
{
    switch (select_clock(clock)) {
    case CLOCK_SOURCE_SYSTEM: {
        runtime_system_clock_instant instant;
        runtime_system_clock_now(&instant);
        time->tv_sec = (time_t)instant.seconds;
        time->tv_nsec = (long)instant.nanoseconds;
        return 0;
    }
    case CLOCK_SOURCE_MONOTONIC:
        from_nanoseconds(runtime_monotonic_clock_now(), time);
        return 0;
    default:
        return EINVAL;
    }
}

int runtime_read_clock_resolution(clockid_t clock, struct timespec *resolution)
{
    switch (select_clock(clock)) {
    case CLOCK_SOURCE_SYSTEM:
        from_nanoseconds(runtime_system_clock_resolution(), resolution);
        return 0;
    case CLOCK_SOURCE_MONOTONIC:
        from_nanoseconds(runtime_monotonic_clock_resolution(), resolution);
        return 0;
    default:
        return EINVAL;
    }
}

int runtime_read_entropy(void *buffer, size_t length)
{
    unsigned char *bytes = buffer;
    while (length != 0) {
        uint64_t random = runtime_get_random_u64();
        size_t count = length < sizeof(random) ? length : sizeof(random);
        memcpy(bytes, &random, count);
        bytes += count;
        length -= count;
    }
    return 0;
}
