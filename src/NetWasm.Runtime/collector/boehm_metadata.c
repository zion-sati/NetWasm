#include "collector_metadata.h"

#include <gc.h>

void *collector_allocate_metadata(size_t size)
{
    /* Frame and handle bookkeeping can run before managed roots are published.
       Keep collection at the compiler's managed safepoints. The collector's
       nested disable count preserves an existing caller/environment setting. */
    GC_disable();
    void *allocation = GC_malloc_atomic_uncollectable(size);
    GC_enable();
    return allocation;
}

void collector_release_metadata(void *address)
{
    GC_free(address);
}
