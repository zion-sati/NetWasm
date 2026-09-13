#include "collector_metadata.h"

#include <assert.h>

static unsigned disable_depth;
static unsigned collections;
static unsigned allocation_calls;
static unsigned release_calls;
static size_t requested_size;
static void *allocation_result;
static void *released_address;

void GC_disable(void) { disable_depth++; }
void GC_enable(void)
{
    assert(disable_depth != 0);
    disable_depth--;
}
void *GC_malloc_atomic_uncollectable(size_t size)
{
    allocation_calls++;
    requested_size = size;
    if (disable_depth == 0) collections++;
    return allocation_result;
}
void GC_free(void *address)
{
    release_calls++;
    released_address = address;
}

int main(void)
{
    unsigned char storage[32];
    for (unsigned initial_depth = 0; initial_depth < 3; initial_depth++) {
        for (unsigned succeeds = 0; succeeds < 2; succeeds++) {
            disable_depth = initial_depth;
            allocation_result = succeeds ? storage : NULL;
            assert(collector_allocate_metadata(sizeof(storage)) == allocation_result);
            assert(requested_size == sizeof(storage));
            assert(disable_depth == initial_depth);
            assert(collections == 0);
        }
    }
    assert(allocation_calls == 6);

    collector_release_metadata(storage);
    assert(released_address == storage);
    collector_release_metadata(NULL);
    assert(released_address == NULL);
    assert(release_calls == 2);
    return 0;
}
