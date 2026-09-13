#include <stddef.h>
#define NETWASM_WASM_PAGE_SIZE ((size_t)65536)

__attribute__((noreturn)) void _abort_js(void)
{
    __builtin_trap();
}

int emscripten_resize_heap(size_t requested_size)
{
    size_t requested_pages = requested_size / NETWASM_WASM_PAGE_SIZE;
    if (requested_size % NETWASM_WASM_PAGE_SIZE != 0) {
        requested_pages++;
    }

    size_t current_pages = __builtin_wasm_memory_size(0);
    if (requested_pages <= current_pages) {
        return 1;
    }

    size_t previous_pages = __builtin_wasm_memory_grow(0, requested_pages - current_pages);
    return previous_pages != (size_t)-1;
}
