// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <assert.h>
#include <dirent.h>
#include <errno.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <sys/types.h>

int __syscall_getuid32(void);
int __syscall_geteuid32(void);
int __syscall_getgid32(void);
int __syscall_getegid32(void);
int __syscall_getdents64(int descriptor, intptr_t entries, size_t length);
int __syscall_unlinkat(int directory, intptr_t path, int flags);
int __syscall_faccessat(int directory, intptr_t path, int mode, int flags);
int __syscall_readlinkat(int directory, intptr_t path, intptr_t buffer, size_t size);
int __syscall_rmdir(intptr_t path);
int __syscall_utimensat(int directory, intptr_t path, intptr_t times, int flags);
int __syscall_getcwd(intptr_t buffer, size_t size);
int __syscall_ftruncate64(int descriptor, off_t length);
int __syscall_fchown32(int descriptor, uid_t owner, gid_t group);
void *dlopen(const char *file, int mode);
void *__dlsym(void *restrict handle, const char *restrict name, void *restrict caller);

int main(void)
{
    assert(__syscall_getuid32() == 0);
    assert(__syscall_geteuid32() == 0);
    assert(__syscall_getgid32() == 0);
    assert(__syscall_getegid32() == 0);
    char entries[64];
    assert(__syscall_getdents64(3, (intptr_t)entries, sizeof entries) == -ENOSYS);
    assert(__syscall_unlinkat(-100, (intptr_t)"file", 0) == -ENOSYS);
    /* Paths fail without a file system; only the standard descriptors are open, and they cannot be resized or change
     * owner; nothing can be loaded. */
    char buffer[16];
    assert(__syscall_faccessat(-100, (intptr_t)"file", 0, 0) == -ENOSYS);
    assert(__syscall_readlinkat(-100, (intptr_t)"link", (intptr_t)buffer, sizeof buffer) == -ENOSYS);
    assert(__syscall_rmdir((intptr_t)"directory") == -ENOSYS);
    assert(__syscall_utimensat(-100, (intptr_t)"file", 0, 0) == -ENOSYS);
    assert(__syscall_getcwd((intptr_t)buffer, sizeof buffer) == -ENOSYS);
    assert(__syscall_ftruncate64(1, 0) == -EINVAL);
    assert(__syscall_ftruncate64(3, 0) == -EBADF);
    assert(__syscall_fchown32(2, 0, 0) == -ENOSYS);
    assert(__syscall_fchown32(7, 0, 0) == -EBADF);
    assert(dlopen("libextension.so", 0) == NULL);
    assert(__dlsym(NULL, "function", NULL) == NULL);
    errno = 0;
    assert(fopen("file", "rb") == NULL && errno == ENOSYS);
    errno = 0;
    assert(opendir(".") == NULL && errno == ENOSYS);
    return 0;
}
