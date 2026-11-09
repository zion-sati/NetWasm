#ifndef NETWASM_WASI_IMPORT_ATTRIBUTES_H
#define NETWASM_WASI_IMPORT_ATTRIBUTES_H

/* A WIT function import, in wit-component's standard names: the target's
 * canonical ABI prefix, then the interface at its compatibility version. */
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

/* A Component Model built-in. The standard names define none, so built-ins
 * use wit-component's full names: `$root`, or the interface at its full
 * version for a built-in of one of its functions' streams or futures. */
#if defined(__wasm__)
#define NETWASM_COMPONENT_BUILTIN(module_name, function_name) \
    __attribute__((import_module(module_name), import_name(function_name)))
#else
#define NETWASM_COMPONENT_BUILTIN(module_name, function_name)
#endif

#endif
