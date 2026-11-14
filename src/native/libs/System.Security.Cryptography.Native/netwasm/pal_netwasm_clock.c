// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: OpenSSL's clock, from the managed clock.
//
// OpenSSL reads the time (time, gettimeofday, clock_gettime) for certificate validity, DRBG reseeding and its own
// timers. Emscripten's C library answers through a WASI Preview 1 import that NetWasm does not use, so these
// definitions of musl's __clock_gettime and clock_gettime ask a managed [UnmanagedCallersOnly] callback instead, over
// DateTime.UtcNow and Stopwatch, which the program's clock grants govern. Emscripten's time and gettimeofday reach
// them. System.Security.Cryptography registers the callback (NetWasmCryptoNative_SetClock) before OpenSSL
// initializes; this object is linked only with the shim.

#include "pal_netwasm_clock.h"

#include <errno.h>
#include <stddef.h>
#include <time.h>

// The managed callback: kind 0 is the wall clock (since the Unix epoch), 1 the monotonic clock. Nonzero on success.
static int32_t (*s_getTime)(int32_t kind, int64_t* seconds, int32_t* nanoseconds);

void NetWasmCryptoNative_SetClock(int32_t (*source)(int32_t kind, int64_t* seconds, int32_t* nanoseconds))
{
    s_getTime = source;
}

int __clock_gettime(clockid_t clock, struct timespec* time)
{
    int32_t kind;
    switch (clock)
    {
        case CLOCK_REALTIME:
        case CLOCK_REALTIME_COARSE:
            kind = 0;
            break;
        case CLOCK_MONOTONIC:
        case CLOCK_MONOTONIC_RAW:
        case CLOCK_MONOTONIC_COARSE:
        case CLOCK_BOOTTIME:
        case CLOCK_PROCESS_CPUTIME_ID:
        case CLOCK_THREAD_CPUTIME_ID:
            kind = 1;
            break;
        default:
            errno = EINVAL;
            return -1;
    }

    int64_t seconds;
    int32_t nanoseconds;
    if (s_getTime == NULL || !s_getTime(kind, &seconds, &nanoseconds))
    {
        errno = ENOTSUP;
        return -1;
    }

    time->tv_sec = (time_t)seconds;
    time->tv_nsec = nanoseconds;
    return 0;
}

int clock_gettime(clockid_t clock, struct timespec* time)
{
    return __clock_gettime(clock, time);
}
