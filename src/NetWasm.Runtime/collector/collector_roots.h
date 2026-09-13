#ifndef NETWASM_COLLECTOR_ROOTS_H
#define NETWASM_COLLECTOR_ROOTS_H

#include <stddef.h>

typedef void (*NetWasmCollectorRootEnumerator)(void);

void collector_register_roots(void *start, size_t size);
void collector_unregister_roots(void *start, size_t size);
void collector_register_root_range(void *start, void *end);
void collector_unregister_root_range(void *start, void *end);
void collector_set_root_enumerator(NetWasmCollectorRootEnumerator enumerator);
void collector_trace_root_range(void *start, void *end);
void collector_clear_roots(void);

#endif
