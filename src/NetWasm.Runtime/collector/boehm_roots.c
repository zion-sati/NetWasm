#include "collector_roots.h"

#include <gc.h>
#include <gc_mark.h>
#include <stdint.h>
#include <stdlib.h>

static NetWasmCollectorRootEnumerator root_enumerator;
static GC_push_other_roots_proc previous_root_enumerator;

static void collector_enumerate_roots(void)
{
    if (previous_root_enumerator != NULL) {
        previous_root_enumerator();
    }
    root_enumerator();
}

void collector_register_roots(void *start, size_t size)
{
    GC_add_roots(start, (uint8_t *)start + size);
}

void collector_unregister_roots(void *start, size_t size)
{
    GC_remove_roots(start, (uint8_t *)start + size);
}

void collector_register_root_range(void *start, void *end)
{
    GC_add_roots(start, end);
}

void collector_unregister_root_range(void *start, void *end)
{
    GC_remove_roots(start, end);
}

void collector_set_root_enumerator(NetWasmCollectorRootEnumerator enumerator)
{
    if (enumerator == NULL || root_enumerator != NULL) {
        abort();
    }
    previous_root_enumerator = GC_get_push_other_roots();
    root_enumerator = enumerator;
    GC_set_push_other_roots(collector_enumerate_roots);
}

void collector_trace_root_range(void *start, void *end)
{
    GC_push_all(start, end);
}

void collector_clear_roots(void)
{
    GC_clear_roots();
}
