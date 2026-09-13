#ifndef NETWASM_RUNTIME_STANDARD_OUTPUT_H
#define NETWASM_RUNTIME_STANDARD_OUTPUT_H

#include <stddef.h>
#include <sys/types.h>
#include <sys/uio.h>

ssize_t runtime_write_standard_output(int descriptor, const void *contents, size_t length);
ssize_t runtime_write_standard_output_vectors(int descriptor, const struct iovec *vectors, int count);
int runtime_close_standard_output(int descriptor);
off_t runtime_seek_standard_output(int descriptor, off_t offset, int whence);

#endif
