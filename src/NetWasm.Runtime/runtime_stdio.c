/*
 * Derived from musl's __stdio_write, __stdio_close and __stdio_seek.
 * Copyright (c) 2005-2020 Rich Felker, et al.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * NetWasm adaptation: retain musl buffering and error behavior, but delegate
 * to the ordinary POSIX entry points backed by the Preview 2 stream adapter.
 * Native AIO is outside this runtime's supported profile.
 */
#include "musl/src/include/features.h"
#include "stdio_impl.h"
#include <sys/uio.h>
#include <unistd.h>

size_t __stdio_write(FILE *f, const unsigned char *buf, size_t len)
{
    struct iovec iovs[2] = {
        { .iov_base = f->wbase, .iov_len = f->wpos - f->wbase },
        { .iov_base = (void *)buf, .iov_len = len }
    };
    struct iovec *iov = iovs;
    size_t rem = iov[0].iov_len + iov[1].iov_len;
    int iovcnt = 2;

    if (!iov->iov_len) {
        ++iov;
        --iovcnt;
    }
    for (;;) {
        ssize_t cnt = writev(f->fd, iov, iovcnt);
        if (cnt >= 0 && (size_t)cnt == rem) {
            f->wend = f->buf + f->buf_size;
            f->wpos = f->wbase = f->buf;
            return len;
        }
        if (cnt < 0) {
            f->wpos = f->wbase = f->wend = 0;
            f->flags |= F_ERR;
            return iovcnt == 2 ? 0 : len - iov[0].iov_len;
        }
        rem -= (size_t)cnt;
        if ((size_t)cnt > iov[0].iov_len) {
            cnt -= (ssize_t)iov[0].iov_len;
            ++iov;
            --iovcnt;
        }
        iov[0].iov_base = (char *)iov[0].iov_base + cnt;
        iov[0].iov_len -= (size_t)cnt;
    }
}

int __stdio_close(FILE *f)
{
    return close(f->fd);
}

off_t __stdio_seek(FILE *f, off_t offset, int whence)
{
    return lseek(f->fd, offset, whence);
}
