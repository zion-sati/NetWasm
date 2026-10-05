#include "collector_lifecycle.h"
#include "tcms_runtime_state.h"
#include "../runtime_libc_initializer.h"
#include <stdlib.h>

Tcms netwasm_tcms;
uint64_t netwasm_tcms_collections;
uint64_t netwasm_tcms_allocated_bytes;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    runtime_initialize_libc();
    if (alignment > _Alignof(max_align_t)) abort();
    return malloc(bytes);
}
static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes; (void)alignment;
    free(address);
}

void collector_initialize(void)
{
    if (tcms_initialize(&netwasm_tcms, allocate_memory, release_memory, 64 * 1024) != TCMS_OK) abort();
    netwasm_tcms.trace_roots = netwasm_tcms_trace_roots;
}
/* These Boehm configuration verbs are ABI-compatible no-ops. The candidate
 * always resolves known managed byrefs, never scans stacks, invokes callbacks
 * on demand, and uses the reviewed non-ordered finalization phases. */
void collector_enable_interior_pointers(void) { }
void collector_disable_stack_scanning(void) { }
void collector_enable_on_demand_finalization(void) { }
void collector_enable_java_finalization_order(void) { }
