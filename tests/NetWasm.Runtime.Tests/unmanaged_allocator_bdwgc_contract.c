#include <assert.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#ifdef __wasm__
/* Keep the unit module independent of libc's diagnostic host imports. */
#undef assert
#define assert(condition) ((condition) ? (void)0 : __builtin_trap())
#endif

static int collector_initialized;
static int fail_bootstrap_allocation;
static int fail_collector_allocation;
static unsigned bootstrap_allocations;
static unsigned bootstrap_frees;
static unsigned collector_allocations;
static unsigned collector_frees;
static void *last_bootstrap_block;
static void *last_collector_block;
static void *collector_storage;
static unsigned libc_initializations;

void runtime_initialize_libc(void)
{
    ++libc_initializations;
}

int GC_is_init_called(void)
{
    assert(libc_initializations == bootstrap_allocations + collector_allocations + 1);
    return collector_initialized;
}

void *GC_malloc_atomic_uncollectable(size_t size)
{
    /* Asking BDWGC for storage here would recursively initialize it. */
    assert(collector_initialized);
    ++collector_allocations;
    /* BDWGC's minimum alignment need not equal C max_align_t. */
    collector_storage = fail_collector_allocation
        ? NULL : malloc(size + _Alignof(max_align_t));
    last_collector_block = collector_storage == NULL ? NULL :
        (unsigned char *)collector_storage + _Alignof(max_align_t) / 2;
    return last_collector_block;
}

void GC_free(void *address)
{
    assert(address == last_collector_block);
    ++collector_frees;
    free(collector_storage);
}

static void *allocate_bootstrap(size_t size)
{
    assert(!collector_initialized);
    ++bootstrap_allocations;
    last_bootstrap_block = fail_bootstrap_allocation ? NULL : malloc(size);
    return last_bootstrap_block;
}

static void free_bootstrap(void *address)
{
    assert(address == last_bootstrap_block);
    ++bootstrap_frees;
    free(address);
}

/* Substitute only the C allocator used by the adapter, not the GC double. */
#define malloc allocate_bootstrap
#define free free_bootstrap
#include "unmanaged_allocator_bdwgc.h"
#undef free
#undef malloc

int main(void)
{
    (void)allocate_bootstrap;
    (void)free_bootstrap;
    /* Early canonical buffers must not initialize or allocate through BDWGC. */
    unsigned char *early = netwasm_unmanaged_allocate(37);
    assert(early != NULL);
    assert((uintptr_t)early % _Alignof(max_align_t) == 0);
    assert(bootstrap_allocations == 1);
    assert(collector_allocations == 0);
    memset(early, 0xa5, 37);
    netwasm_unmanaged_free(early);
    assert(bootstrap_frees == 1);
    assert(collector_frees == 0);

    /* A buffer may outlive startup: free uses its original allocator. */
    early = netwasm_unmanaged_allocate(37);
    assert(early != NULL);
    memset(early, 0x5a, 37);
    collector_initialized = 1;
    unsigned char *late = netwasm_unmanaged_allocate(91);
    assert(late != NULL);
    assert((uintptr_t)late % _Alignof(max_align_t) == 0);
    assert(bootstrap_allocations == 2);
    assert(collector_allocations == 1);
    memset(late, 0xc3, 91);
    for (size_t index = 0; index < 37; ++index) {
        assert(early[index] == 0x5a);
    }
    netwasm_unmanaged_free(early);
    assert(bootstrap_frees == 2);
    assert(collector_frees == 0);
    for (size_t index = 0; index < 91; ++index) {
        assert(late[index] == 0xc3);
    }
    netwasm_unmanaged_free(late);
    assert(collector_frees == 1);

    /* Both allocation failures are explicit, without a fallback. */
    fail_collector_allocation = 1;
    assert(netwasm_unmanaged_allocate(12) == NULL);
    assert(collector_allocations == 2);
    assert(bootstrap_allocations == 2);
    collector_initialized = 0;
    fail_bootstrap_allocation = 1;
    assert(netwasm_unmanaged_allocate(12) == NULL);
    assert(bootstrap_allocations == 3);
    assert(collector_allocations == 2);

    /* Size overflow must fail before calling either allocator. */
    assert(netwasm_unmanaged_allocate(SIZE_MAX) == NULL);
    collector_initialized = 1;
    assert(netwasm_unmanaged_allocate(SIZE_MAX) == NULL);
    assert(bootstrap_allocations == 3);
    assert(collector_allocations == 2);

    netwasm_unmanaged_free(NULL);
    assert(libc_initializations == 5);
    assert(bootstrap_frees == 2);
    assert(collector_frees == 1);
    return 0;
}
