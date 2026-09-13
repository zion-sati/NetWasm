#ifndef NETWASM_TEST_STDIO_IMPL_H
#define NETWASM_TEST_STDIO_IMPL_H

#include <stddef.h>
#include <sys/types.h>

#define F_ERR 32

/* Field-level stdio contract double; real layout is checked by Wasm builds. */
typedef struct {
    unsigned flags;
    unsigned char *wend, *wpos, *wbase, *buf;
    size_t buf_size;
    int fd;
} FILE;

size_t __stdio_write(FILE *file, const unsigned char *contents, size_t length);
int __stdio_close(FILE *file);
off_t __stdio_seek(FILE *file, off_t offset, int whence);

#endif
