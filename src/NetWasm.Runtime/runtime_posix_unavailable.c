// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <stdarg.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <sys/types.h>
#include <dlfcn.h>
#include <sys/uio.h>
#include <unistd.h>

/*
 * Completes Emscripten's standalone-mode stubs (system/lib/standalone/standalone.c) for what native libraries reach
 * through the C library. Native code has no file system: standalone.c's openat fails with ENOSYS, and so do the
 * directory reads and unlinks here. fopen and opendir fail the same way without musl's close of a descriptor they
 * never had, which would import WASI Preview 1. WASI has no users, so the identity calls answer 0, as Emscripten's
 * JavaScript library and NetWasm's CoreLib answer. All are weak, as standalone.c's are, and stay out of a program
 * that no native library takes them into.
 *
 * Every other C library function whose musl version imports WASI Preview 1 is replaced here too. Native code has no
 * descriptors but standard input, output and error: reads fail as __stdio_read does (EBADF), positional I/O on them
 * fails as on a pipe (ESPIPE), and syncing them as on Linux (EINVAL). Temporary files, the time zone database file
 * and network interfaces and DNS fail as an unavailable file system and network do.
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

static int is_standard_descriptor(int descriptor)
{
    return descriptor >= STDIN_FILENO && descriptor <= STDERR_FILENO;
}

static ssize_t fail_read(int descriptor)
{
    (void)descriptor;
    errno = EBADF;
    return -1;
}

static ssize_t fail_positional(int descriptor)
{
    errno = is_standard_descriptor(descriptor) ? ESPIPE : EBADF;
    return -1;
}

__attribute__((weak)) ssize_t read(int descriptor, void *buffer, size_t length)
{
    (void)buffer;
    (void)length;
    return fail_read(descriptor);
}

__attribute__((weak)) ssize_t readv(int descriptor, const struct iovec *vectors, int count)
{
    (void)vectors;
    (void)count;
    return fail_read(descriptor);
}

__attribute__((weak)) ssize_t pread(int descriptor, void *buffer, size_t length, off_t offset)
{
    (void)buffer;
    (void)length;
    (void)offset;
    return fail_positional(descriptor);
}

__attribute__((weak)) ssize_t preadv(int descriptor, const struct iovec *vectors, int count, off_t offset)
{
    (void)vectors;
    (void)count;
    (void)offset;
    return fail_positional(descriptor);
}

__attribute__((weak)) ssize_t pwrite(int descriptor, const void *buffer, size_t length, off_t offset)
{
    (void)buffer;
    (void)length;
    (void)offset;
    return fail_positional(descriptor);
}

__attribute__((weak)) ssize_t pwritev(int descriptor, const struct iovec *vectors, int count, off_t offset)
{
    (void)vectors;
    (void)count;
    (void)offset;
    return fail_positional(descriptor);
}

__attribute__((weak)) int fsync(int descriptor)
{
    errno = is_standard_descriptor(descriptor) ? EINVAL : EBADF;
    return -1;
}

/* The standard descriptors' flags: standard input reads, the others write; nothing else is open. */
__attribute__((weak)) int fcntl(int descriptor, int command, ...)
{
    if (!is_standard_descriptor(descriptor)) {
        errno = EBADF;
        return -1;
    }
    switch (command) {
    case F_GETFD:
    case F_SETFD:
    case F_SETFL:
        return 0;
    case F_GETFL:
        return descriptor == STDIN_FILENO ? O_RDONLY : O_WRONLY;
    default:
        errno = EINVAL;
        return -1;
    }
}

__attribute__((weak)) int isatty(int descriptor)
{
    errno = is_standard_descriptor(descriptor) ? ENOTTY : EBADF;
    return 0;
}

__attribute__((weak)) int dup2(int descriptor, int replacement)
{
    (void)replacement;
    errno = is_standard_descriptor(descriptor) ? ENOSYS : EBADF;
    return -1;
}

__attribute__((weak)) int fchdir(int descriptor)
{
    errno = is_standard_descriptor(descriptor) ? ENOTDIR : EBADF;
    return -1;
}

__attribute__((weak)) int fchmod(int descriptor, mode_t mode)
{
    (void)mode;
    errno = is_standard_descriptor(descriptor) ? ENOSYS : EBADF;
    return -1;
}

__attribute__((weak)) int fchmodat(int directory, const char *path, mode_t mode, int flags)
{
    (void)directory;
    (void)path;
    (void)mode;
    (void)flags;
    errno = ENOSYS;
    return -1;
}

/* The path calls SQLite and other libraries make: without a file system each fails as openat does. */
__attribute__((weak)) int __syscall_faccessat(int directory, intptr_t path, int mode, int flags)
{
    (void)directory;
    (void)path;
    (void)mode;
    (void)flags;
    return -ENOSYS;
}

__attribute__((weak)) int __syscall_readlinkat(int directory, intptr_t path, intptr_t buffer, size_t size)
{
    (void)directory;
    (void)path;
    (void)buffer;
    (void)size;
    return -ENOSYS;
}

__attribute__((weak)) int __syscall_rmdir(intptr_t path)
{
    (void)path;
    return -ENOSYS;
}

__attribute__((weak)) int __syscall_utimensat(int directory, intptr_t path, intptr_t times, int flags)
{
    (void)directory;
    (void)path;
    (void)times;
    (void)flags;
    return -ENOSYS;
}

/* There is no working directory without a file system. */
__attribute__((weak)) int __syscall_getcwd(intptr_t buffer, size_t size)
{
    (void)buffer;
    (void)size;
    return -ENOSYS;
}

/* Only the standard descriptors are open, and neither can be resized or change owner. */
__attribute__((weak)) int __syscall_ftruncate64(int descriptor, off_t length)
{
    (void)length;
    return is_standard_descriptor(descriptor) ? -EINVAL : -EBADF;
}

__attribute__((weak)) int __syscall_fchown32(int descriptor, uid_t owner, gid_t group)
{
    (void)owner;
    (void)group;
    return is_standard_descriptor(descriptor) ? -ENOSYS : -EBADF;
}

/* A program links its native code statically, so there is nothing to load: dlopen finds no library and dlsym no
 * symbol, as for a missing one. SQLite's loadable extensions take this path. */
__attribute__((weak)) void *dlopen(const char *file, int mode)
{
    (void)file;
    (void)mode;
    return NULL;
}

__attribute__((weak)) void *__dlsym(void *restrict handle, const char *restrict name, void *restrict caller)
{
    (void)handle;
    (void)name;
    (void)caller;
    return NULL;
}

__attribute__((weak)) FILE *tmpfile(void)
{
    errno = ENOSYS;
    return NULL;
}

/* musl maps the time zone database (/etc/localtime and TZ files) through this; without a file system local time is
 * UTC, as with a missing database. */
__attribute__((weak)) const unsigned char *__map_file(const char *path, size_t *size)
{
    (void)path;
    (void)size;
    errno = ENOSYS;
    return NULL;
}

__attribute__((weak)) unsigned int if_nametoindex(const char *name)
{
    (void)name;
    errno = ENODEV;
    return 0;
}

__attribute__((weak)) char *if_indextoname(unsigned int index, char *name)
{
    (void)index;
    (void)name;
    errno = ENXIO;
    return NULL;
}

/* getifaddrs and if_nameindex enumerate interfaces through netlink; there is no network. */
__attribute__((weak)) int __rtnetlink_enumerate(int link_family, int address_family, int (*callback)(void *, void *),
    void *context)
{
    (void)link_family;
    (void)address_family;
    (void)callback;
    (void)context;
    errno = ENOSYS;
    return -1;
}

/* DNS queries for getaddrinfo and res_query; there is no network. */
__attribute__((weak)) int __res_msend_rc(int count, const unsigned char *const *queries, const int *query_lengths,
    unsigned char *const *answers, int *answer_lengths, int answer_size, const void *configuration)
{
    (void)count;
    (void)queries;
    (void)query_lengths;
    (void)answers;
    (void)answer_lengths;
    (void)answer_size;
    (void)configuration;
    errno = ENOSYS;
    return -1;
}

__attribute__((weak)) int __res_msend(int count, const unsigned char *const *queries, const int *query_lengths,
    unsigned char *const *answers, int *answer_lengths, int answer_size)
{
    return __res_msend_rc(count, queries, query_lengths, answers, answer_lengths, answer_size, NULL);
}
