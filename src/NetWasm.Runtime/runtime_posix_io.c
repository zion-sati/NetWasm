#include <unistd.h>
#include "runtime_standard_output.h"

ssize_t write(int descriptor, const void *contents, size_t length)
{
    return runtime_write_standard_output(descriptor, contents, length);
}

ssize_t writev(int descriptor, const struct iovec *vectors, int count)
{
    return runtime_write_standard_output_vectors(descriptor, vectors, count);
}

int close(int descriptor)
{
    return runtime_close_standard_output(descriptor);
}

off_t __lseek(int descriptor, off_t offset, int whence)
{
    return runtime_seek_standard_output(descriptor, offset, whence);
}

off_t lseek(int descriptor, off_t offset, int whence)
{
    return runtime_seek_standard_output(descriptor, offset, whence);
}
