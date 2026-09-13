#include <assert.h>
#include <string.h>
#include <sys/uio.h>
#include <unistd.h>
#include "stdio_impl.h"

static ssize_t results[3];
static size_t result_count;
static size_t calls;
static char observed[16];
static size_t observed_length;
static int closed_descriptor;
static int seek_descriptor;
static off_t seek_offset;
static int seek_whence;

ssize_t writev(int descriptor, const struct iovec *vectors, int count)
{
    assert(descriptor == 2);
    assert(calls < result_count);
    ssize_t result = results[calls++];
    if (result > 0) {
        size_t remaining = (size_t)result;
        for (int index = 0; index < count && remaining != 0; ++index) {
            size_t length = remaining < vectors[index].iov_len
                ? remaining : vectors[index].iov_len;
            assert(observed_length + length <= sizeof(observed));
            memcpy(observed + observed_length, vectors[index].iov_base, length);
            observed_length += length;
            remaining -= length;
        }
        assert(remaining == 0);
    }
    return result;
}

int close(int descriptor) { closed_descriptor = descriptor; return -7; }
off_t lseek(int descriptor, off_t offset, int whence)
{
    seek_descriptor = descriptor;
    seek_offset = offset;
    seek_whence = whence;
    return 123;
}

static void check(size_t buffered, const ssize_t *sequence, size_t count,
                  size_t expected, const char *written, int failed)
{
    unsigned char buffer[8] = "abc";
    FILE file = {.flags = 1, .wend = buffer + sizeof(buffer),
        .wpos = buffer + buffered, .wbase = buffer, .buf = buffer,
        .buf_size = sizeof(buffer), .fd = 2};
    memcpy(results, sequence, count * sizeof(*sequence));
    result_count = count;
    calls = observed_length = 0;
    assert(__stdio_write(&file, (const unsigned char *)"defg", 4) == expected);
    assert(calls == count);
    assert(observed_length == strlen(written));
    assert(memcmp(observed, written, observed_length) == 0);
    if (failed) {
        assert(file.wpos == NULL && file.wbase == NULL && file.wend == NULL);
        assert(file.flags == (1 | F_ERR));
    } else {
        assert(file.wpos == buffer && file.wbase == buffer);
        assert(file.wend == buffer + sizeof(buffer));
        assert(file.flags == 1);
    }
}

int main(void)
{
    check(0, (ssize_t[]){4}, 1, 4, "defg", 0);
    check(3, (ssize_t[]){7}, 1, 4, "abcdefg", 0);
    check(3, (ssize_t[]){1, 3, 3}, 3, 4, "abcdefg", 0);
    check(3, (ssize_t[]){-1}, 1, 0, "", 1);
    check(3, (ssize_t[]){4, -1}, 2, 1, "abcd", 1);
    check(0, (ssize_t[]){1, -1}, 2, 1, "d", 1);
    FILE file = {.fd = 2};
    assert(__stdio_close(&file) == -7 && closed_descriptor == 2);
    assert(__stdio_seek(&file, 77, 2) == 123);
    assert(seek_descriptor == 2 && seek_offset == 77 && seek_whence == 2);
    return 0;
}
