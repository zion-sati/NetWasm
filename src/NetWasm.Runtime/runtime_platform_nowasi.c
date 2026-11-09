/* The runtime's platform functions for a plain core module, which has no WASI: no environment,
 * standard output and error that discard what is written, an exit that traps, and a fixed hash
 * seed. Linked whole ahead of the runtime archive, these definitions replace the WASI ones
 * (runtime_environment_reader.c, runtime_standard_output.c, runtime_process_exit.c and
 * runtime_random_seed.c), so the module imports nothing from WASI. */
#include <errno.h>
#include <stdint.h>
#include <stdlib.h>
#include <sys/uio.h>
#include <unistd.h>

#include "runtime_environment_reader.h"
#include "runtime_process_exit.h"
#include "runtime_standard_output.h"

#define NETWASM_SSIZE_MAX (SIZE_MAX >> 1)

static int closed[2];

char **runtime_read_environment(void)
{
    char **environment = calloc(1, sizeof(char *));
    if (environment == NULL) {
        abort();
    }
    return environment;
}

static int resolve_output(int descriptor)
{
    if (descriptor < STDOUT_FILENO || descriptor > STDERR_FILENO ||
        closed[descriptor - STDOUT_FILENO]) {
        errno = EBADF;
        return 0;
    }
    return 1;
}

ssize_t runtime_write_standard_output(int descriptor, const void *contents, size_t length)
{
    if (!resolve_output(descriptor)) {
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
    return (ssize_t)length;
}

ssize_t runtime_write_standard_output_vectors(int descriptor, const struct iovec *vectors, int count)
{
    if (!resolve_output(descriptor)) {
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
    return (ssize_t)total;
}

int runtime_close_standard_output(int descriptor)
{
    if (!resolve_output(descriptor)) {
        return -1;
    }
    closed[descriptor - STDOUT_FILENO] = 1;
    return 0;
}

off_t runtime_seek_standard_output(int descriptor, off_t offset, int whence)
{
    (void)offset;
    (void)whence;
    if (!resolve_output(descriptor)) {
        return -1;
    }
    errno = ESPIPE;
    return -1;
}

_Noreturn void runtime_exit_process(int status)
{
    (void)status;
    abort();
}

#if !defined(NETWASM_NO_RANDOM_SEED)
uint64_t random_seed(void)
{
    /* Without WASI there is no entropy to seed with; hashing stays deterministic. */
    return UINT64_C(0x9E3779B97F4A7C15);
}
#endif
