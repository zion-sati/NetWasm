#ifndef NETWASM_WASI_OUTPUT_IMPORTS_H
#define NETWASM_WASI_OUTPUT_IMPORTS_H

#include <stddef.h>
#include <stdint.h>

#include "wasi_import_attributes.h"

/* wasi:cli/stdout and stderr 0.3: write-via-stream takes the readable end of
 * a stream<u8> (index 0) and returns a future<result<_, error-code>>
 * (index 1) that resolves once the host has written everything. Copies are
 * async-lowered: synchronous stream built-ins are outside WASI 0.3's
 * Component Model features. */

#define NETWASM_WASI_OUTPUT_BLOCKED UINT32_MAX
#define NETWASM_WASI_COPY_COMPLETED 0u
#define NETWASM_WASI_COPY_DROPPED 1u

NETWASM_COMPONENT_BUILTIN("wasi:cli/stdout@0.3.2", "[stream-new-0]write-via-stream")
uint64_t runtime_wasi_stdout_stream_new(void);
NETWASM_WASI_IMPORT("wasi:cli/stdout@0.3", "write-via-stream")
uint32_t runtime_wasi_stdout_write_via_stream(uint32_t readable);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stdout@0.3.2", "[async-lower][stream-write-0]write-via-stream")
uint32_t runtime_wasi_stdout_stream_write(uint32_t writable, const void *contents, size_t length);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stdout@0.3.2", "[stream-drop-writable-0]write-via-stream")
void runtime_wasi_stdout_stream_drop_writable(uint32_t writable);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stdout@0.3.2", "[async-lower][future-read-1]write-via-stream")
uint32_t runtime_wasi_stdout_future_read(uint32_t readable, void *result);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stdout@0.3.2", "[future-drop-readable-1]write-via-stream")
void runtime_wasi_stdout_future_drop_readable(uint32_t readable);

NETWASM_COMPONENT_BUILTIN("wasi:cli/stderr@0.3.2", "[stream-new-0]write-via-stream")
uint64_t runtime_wasi_stderr_stream_new(void);
NETWASM_WASI_IMPORT("wasi:cli/stderr@0.3", "write-via-stream")
uint32_t runtime_wasi_stderr_write_via_stream(uint32_t readable);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stderr@0.3.2", "[async-lower][stream-write-0]write-via-stream")
uint32_t runtime_wasi_stderr_stream_write(uint32_t writable, const void *contents, size_t length);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stderr@0.3.2", "[stream-drop-writable-0]write-via-stream")
void runtime_wasi_stderr_stream_drop_writable(uint32_t writable);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stderr@0.3.2", "[async-lower][future-read-1]write-via-stream")
uint32_t runtime_wasi_stderr_future_read(uint32_t readable, void *result);
NETWASM_COMPONENT_BUILTIN("wasi:cli/stderr@0.3.2", "[future-drop-readable-1]write-via-stream")
void runtime_wasi_stderr_future_drop_readable(uint32_t readable);

NETWASM_COMPONENT_BUILTIN("$root", "[waitable-set-new]")
uint32_t runtime_wasi_waitable_set_new(void);
NETWASM_COMPONENT_BUILTIN("$root", "[waitable-join]")
void runtime_wasi_waitable_join(uint32_t waitable, uint32_t set);
NETWASM_COMPONENT_BUILTIN("$root", "[waitable-set-wait]")
uint32_t runtime_wasi_waitable_set_wait(uint32_t set, uint32_t *event);

#endif
