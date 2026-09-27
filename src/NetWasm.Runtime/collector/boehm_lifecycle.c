#include "collector_lifecycle.h"

#include <gc.h>
#ifdef NETWASM_GC_DIAGNOSTICS
#include <stdlib.h>
#endif

void collector_initialize(void)
{
#ifdef NETWASM_GC_DIAGNOSTICS
    /* Keep the no-collection diagnostic out of size-optimized releases. */
    if (getenv("GC_DONT_GC") != NULL) {
        GC_disable();
    }
#endif
    GC_INIT();
}

void collector_enable_interior_pointers(void)
{
    GC_set_all_interior_pointers(1);
}

void collector_disable_stack_scanning(void)
{
    struct GC_stack_base stack_base = {0};
    stack_base.mem_base = 0;
    GC_set_stackbottom(0, &stack_base);
}

void collector_enable_on_demand_finalization(void)
{
    GC_set_finalize_on_demand(1);
}

void collector_enable_java_finalization_order(void)
{
    GC_set_java_finalization(1);
}
