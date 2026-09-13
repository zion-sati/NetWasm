#include <assert.h>
#include <unistd.h>
#include "runtime_standard_output.h"

static int last_descriptor;
static const void *last_contents;
static size_t last_length;
static const struct iovec *last_vectors;
static int last_count;
static off_t last_offset;
static int last_whence;
extern off_t __lseek(int descriptor, off_t offset, int whence);

ssize_t runtime_write_standard_output(int descriptor, const void *contents, size_t length)
{
    last_descriptor = descriptor; last_contents = contents; last_length = length;
    return 7;
}
ssize_t runtime_write_standard_output_vectors(int descriptor, const struct iovec *vectors, int count)
{
    last_descriptor = descriptor; last_vectors = vectors; last_count = count;
    return -1;
}
int runtime_close_standard_output(int descriptor) { last_descriptor = descriptor; return 3; }
off_t runtime_seek_standard_output(int descriptor, off_t offset, int whence)
{
    last_descriptor = descriptor; last_offset = offset; last_whence = whence;
    return -1;
}

int main(void)
{
    const char contents[] = "abc";
    struct iovec vectors[] = {{(void *)contents, 3}};
    assert(write(12, contents, 3) == 7);
    assert(last_descriptor == 12 && last_contents == contents && last_length == 3);
    assert(writev(13, vectors, 1) == -1);
    assert(last_descriptor == 13 && last_vectors == vectors && last_count == 1);
    assert(close(14) == 3 && last_descriptor == 14);
    assert(lseek(15, 64, 2) == -1);
    assert(last_descriptor == 15 && last_offset == 64 && last_whence == 2);
    assert(__lseek(16, 99, 1) == -1);
    assert(last_descriptor == 16 && last_offset == 99 && last_whence == 1);
    return 0;
}
