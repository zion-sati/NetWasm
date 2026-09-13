#ifndef NETWASM_TEST_FAKE_UNMANAGED_ALLOCATOR_H
#define NETWASM_TEST_FAKE_UNMANAGED_ALLOCATOR_H

#include <stddef.h>
#include <stdlib.h>

static unsigned int netwasm_test_allocate_count;
static unsigned int netwasm_test_free_count;

static inline void *netwasm_unmanaged_allocate(size_t size)
{
    ++netwasm_test_allocate_count;
    return malloc(size);
}

static inline void netwasm_unmanaged_free(void *allocation)
{
    ++netwasm_test_free_count;
    free(allocation);
}

#endif
