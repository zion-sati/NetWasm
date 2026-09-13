#ifndef NETWASM_TEST_FAKE_GC_H
#define NETWASM_TEST_FAKE_GC_H

#include <stdint.h>
#include <stddef.h>

extern int fake_short_link_result;
extern int fake_long_link_result;
extern int fake_unregister_result;
extern int fake_unregister_long_result;
extern int fake_short_link_calls;
extern int fake_long_link_calls;
extern int fake_unregister_calls;
extern int fake_unregister_long_calls;
extern void **fake_last_link;
extern const void *fake_last_target;

struct GC_stack_base {
    void *mem_base;
};

#define GC_SUCCESS 0
#define GC_DUPLICATE 1
#define GC_NO_MEMORY 2

int GC_general_register_disappearing_link(void **link, const void *target);
int GC_register_long_link(void **link, const void *target);
int GC_unregister_disappearing_link(void **link);
int GC_unregister_long_link(void **link);
void GC_INIT(void);
void GC_set_all_interior_pointers(int enabled);
void GC_set_stackbottom(void *thread, const struct GC_stack_base *stack_base);
void GC_set_finalize_on_demand(int enabled);
void GC_set_java_finalization(int enabled);
void GC_gcollect(void);
unsigned GC_get_gc_no(void);
uint64_t GC_get_total_bytes(void);
uint64_t GC_get_heap_size(void);
uint64_t GC_get_free_bytes(void);
void GC_disable(void);
void GC_enable(void);
int GC_is_init_called(void);
void *GC_malloc_atomic_uncollectable(size_t size);
void GC_free(void *address);

#endif
