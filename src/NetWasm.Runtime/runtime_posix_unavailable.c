#include <dirent.h>
#include <errno.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

/*
 * Completes Emscripten's standalone-mode stubs (system/lib/standalone/standalone.c) for what native libraries reach
 * through the C library. Native code has no file system: standalone.c's openat fails with ENOSYS, and so do the
 * directory reads and unlinks here. fopen and opendir fail the same way without musl's close of a descriptor they
 * never had, which would import WASI Preview 1. WASI has no users, so the identity calls answer 0, as Emscripten's
 * JavaScript library and NetWasm's CoreLib answer. All are weak, as standalone.c's are, and stay out of a program
 * that no native library takes them into.
 */

__attribute__((weak)) int __syscall_getuid32(void)
{
    return 0;
}

__attribute__((weak)) int __syscall_geteuid32(void)
{
    return 0;
}

__attribute__((weak)) int __syscall_getgid32(void)
{
    return 0;
}

__attribute__((weak)) int __syscall_getegid32(void)
{
    return 0;
}

__attribute__((weak)) int __syscall_getdents64(int descriptor, intptr_t entries, size_t length)
{
    (void)descriptor;
    (void)entries;
    (void)length;
    return -ENOSYS;
}

__attribute__((weak)) int __syscall_unlinkat(int directory, intptr_t path, int flags)
{
    (void)directory;
    (void)path;
    (void)flags;
    return -ENOSYS;
}

__attribute__((weak)) FILE *fopen(const char *restrict path, const char *restrict mode)
{
    (void)path;
    (void)mode;
    errno = ENOSYS;
    return NULL;
}

__attribute__((weak)) DIR *opendir(const char *path)
{
    (void)path;
    errno = ENOSYS;
    return NULL;
}
