#ifndef NETWASM_WASI_OUTPUT_IMPORTS_H
#define NETWASM_WASI_OUTPUT_IMPORTS_H

#include <stddef.h>
#include <stdint.h>
#include "wasi_import_attributes.h"

typedef struct {
    uint8_t is_error;
    uint8_t result_padding[3];
    uint8_t error_kind;
    uint8_t error_padding[3];
    uint32_t error_handle;
} NetWasmWasiOutputResult;

_Static_assert(sizeof(NetWasmWasiOutputResult) == 12,
               "Canonical output result size is target independent");
_Static_assert(offsetof(NetWasmWasiOutputResult, error_handle) == 8,
               "Canonical output error handle must follow both discriminants");

NETWASM_WASI_IMPORT("wasi:cli/stdout@0.2", "get-stdout")
uint32_t runtime_get_wasi_stdout(void);
NETWASM_WASI_IMPORT("wasi:cli/stderr@0.2", "get-stderr")
uint32_t runtime_get_wasi_stderr(void);
NETWASM_WASI_IMPORT("wasi:io/streams@0.2",
                   "[method]output-stream.blocking-write-and-flush")
void runtime_write_wasi_output(uint32_t handle, const void *contents,
                              size_t length, NetWasmWasiOutputResult *result);
NETWASM_WASI_IMPORT("wasi:io/streams@0.2", "output-stream_drop")
void runtime_drop_wasi_output(uint32_t handle);
NETWASM_WASI_IMPORT("wasi:io/error@0.2", "error_drop")
void runtime_drop_wasi_output_error(uint32_t handle);

#endif
