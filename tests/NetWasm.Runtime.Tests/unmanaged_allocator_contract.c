#include <assert.h>

#define NETWASM_UNMANAGED_ALLOCATOR_BACKEND "fake_unmanaged_allocator.h"
#include "unmanaged_allocator.h"

int main(void)
{
    void *allocation = netwasm_unmanaged_allocate(32);
    assert(allocation != 0);
    assert(netwasm_test_allocate_count == 1);

    netwasm_unmanaged_free(allocation);
    assert(netwasm_test_free_count == 1);
    return 0;
}
