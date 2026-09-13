#include <assert.h>

#include "collector_lifecycle.h"
#include "gc.h"

static int initialize_calls;
static int interior_pointers;
static int stack_bottom_calls;
static int finalize_on_demand;
static int java_finalization;

void GC_INIT(void) { initialize_calls++; }
void GC_set_all_interior_pointers(int enabled) { interior_pointers = enabled; }
void GC_set_stackbottom(void *thread, const struct GC_stack_base *stack_base)
{
    assert(thread == 0);
    assert(stack_base != 0);
    assert(stack_base->mem_base == 0);
    stack_bottom_calls++;
}
void GC_set_finalize_on_demand(int enabled) { finalize_on_demand = enabled; }
void GC_set_java_finalization(int enabled) { java_finalization = enabled; }

int main(void)
{
    collector_initialize();
    collector_enable_interior_pointers();
    collector_disable_stack_scanning();
    collector_enable_on_demand_finalization();
    collector_enable_java_finalization_order();

    assert(initialize_calls == 1);
    assert(interior_pointers == 1);
    assert(stack_bottom_calls == 1);
    assert(finalize_on_demand == 1);
    assert(java_finalization == 1);
    return 0;
}
