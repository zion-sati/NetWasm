#include "collector_finalization.h"

#include <gc.h>

void collector_register_finalizer(
    void *object,
    NetWasmCollectorFinalizer finalizer,
    void *context,
    NetWasmCollectorFinalizer *previous_finalizer,
    void **previous_context)
{
    GC_register_finalizer_no_order(
        object,
        finalizer,
        context,
        (GC_finalization_proc *)previous_finalizer,
        previous_context);
}

bool collector_has_pending_finalizers(void)
{
    return GC_should_invoke_finalizers() != 0;
}

int collector_invoke_finalizers(void)
{
    return GC_invoke_finalizers();
}
