#include <errno.h>
#include <stdint.h>
#include <stdlib.h>
#include <unistd.h>

#include "runtime_standard_output.h"
#include "wasi_output_imports.h"

/* WASI's blocking-write-and-flush contract permits at most 4096 bytes. */
#define NETWASM_WASI_BLOCKING_WRITE_LIMIT ((size_t)4096)
#define NETWASM_SSIZE_MAX (SIZE_MAX >> 1)

typedef struct {
    uint32_t handle;
    int acquired;
    int closed;
} StandardOutput;

static StandardOutput outputs[2];

static StandardOutput *resolve_output(int descriptor)
{
    if (descriptor < STDOUT_FILENO || descriptor > STDERR_FILENO ||
        outputs[descriptor - STDOUT_FILENO].closed) {
        errno = EBADF;
        return NULL;
    }
    return &outputs[descriptor - STDOUT_FILENO];
}

ssize_t runtime_write_standard_output(int descriptor, const void *contents, size_t length)
{
    StandardOutput *output = resolve_output(descriptor);
    if (output == NULL) {
        return -1;
    }
    if (length > NETWASM_SSIZE_MAX) {
        errno = EINVAL;
        return -1;
    }
    if (length != 0 && contents == NULL) {
        errno = EFAULT;
        return -1;
    }
    if (length == 0) {
        return 0;
    }
    if (!output->acquired) {
        output->handle = descriptor == STDOUT_FILENO
            ? runtime_get_wasi_stdout() : runtime_get_wasi_stderr();
        output->acquired = 1;
    }
    size_t written = 0;
    while (written < length) {
        size_t remaining = length - written;
        size_t chunk = remaining < NETWASM_WASI_BLOCKING_WRITE_LIMIT
            ? remaining : NETWASM_WASI_BLOCKING_WRITE_LIMIT;
        NetWasmWasiOutputResult result = {0};
        runtime_write_wasi_output(output->handle,
            (const unsigned char *)contents + written, chunk, &result);
        if (result.is_error != 0) {
            if (result.is_error != 1) {
                abort();
            }
            if (result.error_kind == 0) {
                runtime_drop_wasi_output_error(result.error_handle);
                errno = EIO;
            } else if (result.error_kind == 1) {
                errno = EPIPE;
            } else {
                abort();
            }
            return written == 0 ? -1 : (ssize_t)written;
        }
        written += chunk;
    }
    return (ssize_t)written;
}

ssize_t runtime_write_standard_output_vectors(int descriptor, const struct iovec *vectors, int count)
{
    if (resolve_output(descriptor) == NULL) {
        return -1;
    }
    if (count < 0) {
        errno = EINVAL;
        return -1;
    }
    if (count != 0 && vectors == NULL) {
        errno = EFAULT;
        return -1;
    }
    size_t total = 0;
    for (int index = 0; index < count; ++index) {
        if (vectors[index].iov_len > NETWASM_SSIZE_MAX - total) {
            errno = EINVAL;
            return -1;
        }
        if (vectors[index].iov_len != 0 && vectors[index].iov_base == NULL) {
            errno = EFAULT;
            return -1;
        }
        total += vectors[index].iov_len;
    }
    size_t written = 0;
    for (int index = 0; index < count; ++index) {
        ssize_t count_written = runtime_write_standard_output(
            descriptor, vectors[index].iov_base, vectors[index].iov_len);
        if (count_written < 0) {
            return written == 0 ? -1 : (ssize_t)written;
        }
        written += (size_t)count_written;
        if ((size_t)count_written != vectors[index].iov_len) {
            return (ssize_t)written;
        }
    }
    return (ssize_t)written;
}

int runtime_close_standard_output(int descriptor)
{
    StandardOutput *output = resolve_output(descriptor);
    if (output == NULL) {
        return -1;
    }
    if (output->acquired) {
        runtime_drop_wasi_output(output->handle);
    }
    output->closed = 1;
    return 0;
}

off_t runtime_seek_standard_output(int descriptor, off_t offset, int whence)
{
    (void)offset;
    (void)whence;
    if (resolve_output(descriptor) != NULL) {
        errno = ESPIPE;
    }
    return -1;
}
