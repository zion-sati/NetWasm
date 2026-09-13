#ifndef NETWASM_WASI_IMPORT_ATTRIBUTES_H
#define NETWASM_WASI_IMPORT_ATTRIBUTES_H

#if defined(__wasm64__)
#define NETWASM_WASI_IMPORT(interface_name, function_name) \
    __attribute__((import_module("cm64p2|" interface_name), \
                   import_name(function_name)))
#elif defined(__wasm32__)
#define NETWASM_WASI_IMPORT(interface_name, function_name) \
    __attribute__((import_module("cm32p2|" interface_name), \
                   import_name(function_name)))
#else
#define NETWASM_WASI_IMPORT(interface_name, function_name)
#endif

#endif
