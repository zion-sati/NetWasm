#include <assert.h>
#include <dirent.h>
#include <errno.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

int __syscall_getuid32(void);
int __syscall_geteuid32(void);
int __syscall_getgid32(void);
int __syscall_getegid32(void);
int __syscall_getdents64(int descriptor, intptr_t entries, size_t length);
int __syscall_unlinkat(int directory, intptr_t path, int flags);

int main(void)
{
    assert(__syscall_getuid32() == 0);
    assert(__syscall_geteuid32() == 0);
    assert(__syscall_getgid32() == 0);
    assert(__syscall_getegid32() == 0);
    char entries[64];
    assert(__syscall_getdents64(3, (intptr_t)entries, sizeof entries) == -ENOSYS);
    assert(__syscall_unlinkat(-100, (intptr_t)"file", 0) == -ENOSYS);
    errno = 0;
    assert(fopen("file", "rb") == NULL && errno == ENOSYS);
    errno = 0;
    assert(opendir(".") == NULL && errno == ENOSYS);
    return 0;
}
