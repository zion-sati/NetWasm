#include "collector_roots.h"
#include "collector_metadata.h"
#include "tcms_runtime_state.h"
#include <stdlib.h>
#include <string.h>

typedef struct RootRange {
    struct RootRange *next;
    void *start;
    size_t bytes, registrations;
} RootRange;
static RootRange *ranges;
static NetWasmCollectorRootEnumerator enumerate_roots;

void collector_register_roots(void *start, size_t size)
{
    if (start == NULL || size % sizeof(uintptr_t) != 0) abort();
    for (RootRange *entry = ranges; entry != NULL; entry = entry->next) {
        if (entry->start == start && entry->bytes == size) {
            if (entry->registrations == SIZE_MAX) abort();
            entry->registrations++;
            return;
        }
    }
    RootRange *entry = collector_allocate_metadata(sizeof(*entry));
    if (entry == NULL) abort();
    *entry = (RootRange){ranges, start, size, 1};
    ranges = entry;
}
void collector_unregister_roots(void *start, size_t size)
{
    RootRange **link = &ranges;
    while (*link != NULL && ((*link)->start != start || (*link)->bytes != size)) link = &(*link)->next;
    if (*link == NULL) return;
    RootRange *entry = *link;
    if (--entry->registrations != 0) return;
    *link = entry->next;
    collector_release_metadata(entry);
}
void collector_register_root_range(void *start, void *end)
{
    if ((uintptr_t)end < (uintptr_t)start) abort();
    collector_register_roots(start, (uintptr_t)end - (uintptr_t)start);
}
void collector_unregister_root_range(void *start, void *end)
{
    if ((uintptr_t)end < (uintptr_t)start) abort();
    collector_unregister_roots(start, (uintptr_t)end - (uintptr_t)start);
}
void collector_set_root_enumerator(NetWasmCollectorRootEnumerator enumerator)
{
    if (enumerator == NULL || enumerate_roots != NULL) abort();
    enumerate_roots = enumerator;
}
void collector_trace_root_range(void *start, void *end)
{
    uintptr_t first = (uintptr_t)start, limit = (uintptr_t)end;
    if (!netwasm_tcms.collecting || limit < first || (limit - first) % sizeof(uintptr_t) != 0) abort();
    for (uintptr_t slot = first; slot < limit; slot += sizeof(uintptr_t)) {
        uintptr_t reference;
        memcpy(&reference, (void *)slot, sizeof(reference));
        tcms_mark(&netwasm_tcms, reference);
    }
}
void netwasm_tcms_trace_roots(Tcms *gc, void *state)
{
    (void)gc; (void)state;
    netwasm_tcms_collections++;
    for (RootRange *entry = ranges; entry != NULL; entry = entry->next)
        collector_trace_root_range(entry->start, (unsigned char *)entry->start + entry->bytes);
    if (enumerate_roots != NULL) enumerate_roots();
}
void collector_clear_roots(void)
{
    while (ranges != NULL) {
        RootRange *entry = ranges;
        ranges = entry->next;
        collector_release_metadata(entry);
    }
}
