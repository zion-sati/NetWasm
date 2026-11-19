// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

/* Native code that needs Emscripten's runtime support: Wasm setjmp/longjmp, and the C library's clocks and entropy.
 * Built freestanding, so the few declarations it uses are its own, with Emscripten's C ABI. */

typedef long long native_time;
typedef struct { long long tv_sec; long tv_nsec; } native_timespec;
typedef long native_jmp_buf[16];

int setjmp(native_jmp_buf environment);
void longjmp(native_jmp_buf environment, int value) __attribute__((noreturn));
int clock_gettime(int clock, native_timespec *time);
int clock_getres(int clock, native_timespec *resolution);
int getentropy(void *buffer, __SIZE_TYPE__ length);
native_time time(native_time *time);

static native_jmp_buf environment;

static int __attribute__((noinline)) depth(int n)
{
    if (n == 0)
        longjmp(environment, 7);
    return depth(n - 1) + 1;
}

/* A longjmp from n frames down returns 7 from setjmp; the counter set before it survives. */
int native_runtime_jump(int n)
{
    volatile int count = 0;
    int result = setjmp(environment);
    if (result == 0)
    {
        count++;
        depth(n);
        return -1;
    }
    return result * 100 + count;
}

/* longjmp with 0 makes setjmp return 1. */
int native_runtime_jump_zero(void)
{
    native_jmp_buf buffer;
    volatile int result = setjmp(buffer);
    if (result == 0)
        longjmp(buffer, 0);
    return result;
}

/* Two setjmps in one function: the second's longjmp lands at the second. */
int native_runtime_two(void)
{
    native_jmp_buf first, second;
    volatile int trail = 0;
    if (setjmp(first) == 0)
    {
        trail += 1;
        if (setjmp(second) == 0)
        {
            trail += 10;
            longjmp(second, 2);
        }
        trail += 100;
    }
    return trail;
}

/* CLOCK_REALTIME is 0 and CLOCK_MONOTONIC 1 in Emscripten's C library. */
int native_runtime_clock(void)
{
    native_timespec first, second, resolution;
    if (clock_gettime(1, &first) || clock_gettime(1, &second) || clock_getres(0, &resolution) ||
        clock_getres(1, &resolution))
        return -1;
    if (second.tv_sec < first.tv_sec || (second.tv_sec == first.tv_sec && second.tv_nsec < first.tv_nsec))
        return -2;
    native_timespec now;
    if (clock_gettime(0, &now) || now.tv_sec < 1700000000 || clock_gettime(99, &now) == 0)
        return -3;
    return 1;
}

int native_runtime_entropy(void)
{
    unsigned char buffer[64];
    int any = 0;
    if (getentropy(buffer, sizeof buffer) || getentropy(buffer, 257) == 0)
        return -1;
    for (int index = 0; index < 64; index++)
        any |= buffer[index];
    return any != 0;
}

long long native_runtime_time(void)
{
    return time(0);
}
