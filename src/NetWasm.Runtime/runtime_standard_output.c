#include <errno.h>
#include <stdint.h>
#include <stdlib.h>
#include <unistd.h>

#include "runtime_standard_output.h"
#include "wasi_output_imports.h"

#define NETWASM_SSIZE_MAX (SIZE_MAX >> 1)

typedef struct {
    uint32_t writable;
    uint32_t completion;
    int opened;
    int reader_gone;
    int closed;
} StandardOutput;

static StandardOutput outputs[2];
static uint32_t wait_set;

static StandardOutput *resolve_output(int descriptor)
{
    if (descriptor < STDOUT_FILENO || descriptor > STDERR_FILENO ||
        outputs[descriptor - STDOUT_FILENO].closed) {
        errno = EBADF;
        return NULL;
    }

    return &outputs[descriptor - STDOUT_FILENO];
}

/* Blocks until the waitable has an event and returns its payload. No other
 * code runs during the wait, so one set serves every wait. */
static uint32_t wait_for(uint32_t waitable)
{
    uint32_t event[2];
    if (wait_set == 0) {
        wait_set = runtime_wasi_waitable_set_new();
    }

    runtime_wasi_waitable_join(waitable, wait_set);
    runtime_wasi_waitable_set_wait(wait_set, event);
    runtime_wasi_waitable_join(waitable, 0);
    if (event[0] != waitable) {
        abort();
    }

    return event[1];
}

static void open_output(int descriptor, StandardOutput *output)
{
    uint64_t ends = descriptor == STDOUT_FILENO
        ? runtime_wasi_stdout_stream_new() : runtime_wasi_stderr_stream_new();
    uint32_t readable = (uint32_t)ends;
    output->writable = (uint32_t)(ends >> 32);
    output->completion = descriptor == STDOUT_FILENO
        ? runtime_wasi_stdout_write_via_stream(readable)
        : runtime_wasi_stderr_write_via_stream(readable);
    output->opened = 1;
}

static uint32_t copy_output(int descriptor, StandardOutput *output,
                            const unsigned char *contents, size_t length)
{
    uint32_t status = descriptor == STDOUT_FILENO
        ? runtime_wasi_stdout_stream_write(output->writable, contents, length)
        : runtime_wasi_stderr_stream_write(output->writable, contents, length);
    return status == NETWASM_WASI_OUTPUT_BLOCKED ? wait_for(output->writable) : status;
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

    if (output->reader_gone) {
        errno = EPIPE;
        return -1;
    }

    if (!output->opened) {
        open_output(descriptor, output);
    }

    size_t written = 0;
    while (written < length) {
        uint32_t progress = copy_output(descriptor, output,
            (const unsigned char *)contents + written, length - written);
        written += progress >> 4;
        uint32_t result = progress & 0xFu;
        if (result == NETWASM_WASI_COPY_DROPPED) {
            output->reader_gone = 1;
            if (written == 0) {
                errno = EPIPE;
                return -1;
            }

            return (ssize_t)written;
        }

        if (result != NETWASM_WASI_COPY_COMPLETED) {
            abort();
        }
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
    if (output->opened) {
        /* Ending the stream and waiting for the completion future flushes
         * it; a closed console reports no write error, as on desktop. */
        unsigned char result[2];
        if (descriptor == STDOUT_FILENO) {
            runtime_wasi_stdout_stream_drop_writable(output->writable);
            if (runtime_wasi_stdout_future_read(output->completion, result) ==
                NETWASM_WASI_OUTPUT_BLOCKED) {
                wait_for(output->completion);
            }
            runtime_wasi_stdout_future_drop_readable(output->completion);
        } else {
            runtime_wasi_stderr_stream_drop_writable(output->writable);
            if (runtime_wasi_stderr_future_read(output->completion, result) ==
                NETWASM_WASI_OUTPUT_BLOCKED) {
                wait_for(output->completion);
            }
            runtime_wasi_stderr_future_drop_readable(output->completion);
        }
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
