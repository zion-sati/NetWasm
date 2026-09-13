#include <assert.h>
#include <stdint.h>

#include "collector_weak_reference.h"
#include "gc.h"

int fake_short_link_result;
int fake_long_link_result;
int fake_unregister_result;
int fake_unregister_long_result;
int fake_short_link_calls;
int fake_long_link_calls;
int fake_unregister_calls;
int fake_unregister_long_calls;
void **fake_last_link;
const void *fake_last_target;

int GC_general_register_disappearing_link(void **link, const void *target)
{
    fake_short_link_calls++;
    fake_last_link = link;
    fake_last_target = target;
    return fake_short_link_result;
}

int GC_register_long_link(void **link, const void *target)
{
    fake_long_link_calls++;
    fake_last_link = link;
    fake_last_target = target;
    return fake_long_link_result;
}

int GC_unregister_disappearing_link(void **link)
{
    fake_unregister_calls++;
    fake_last_link = link;
    return fake_unregister_result;
}

int GC_unregister_long_link(void **link)
{
    fake_unregister_long_calls++;
    fake_last_link = link;
    return fake_unregister_long_result;
}

int main(void)
{
    uintptr_t slot = 0;

    assert(collector_register_weak_reference(
        &slot,
        0,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 1);
    assert(fake_short_link_calls == 0);
    assert(fake_long_link_calls == 0);

    fake_short_link_result = GC_SUCCESS;
    assert(collector_register_weak_reference(
        &slot,
        0x1234,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 1);
    assert(fake_short_link_calls == 1);
    assert(fake_last_link == (void **)&slot);
    assert(fake_last_target == (const void *)0x1234);

    fake_long_link_result = GC_DUPLICATE;
    assert(collector_register_weak_reference(
        &slot,
        0x5678,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 1);
    assert(fake_long_link_calls == 1);
    assert(fake_last_link == (void **)&slot);
    assert(fake_last_target == (const void *)0x5678);

    fake_short_link_result = GC_NO_MEMORY;
    assert(collector_register_weak_reference(
        &slot,
        0x9abc,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 0);

    fake_long_link_result = GC_NO_MEMORY;
    assert(collector_register_weak_reference(
        &slot,
        0xdef0,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 0);

    fake_unregister_result = 11;
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 11);
    assert(fake_unregister_calls == 1);
    assert(fake_last_link == (void **)&slot);

    fake_unregister_long_result = 13;
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 13);
    assert(fake_unregister_long_calls == 1);
    assert(fake_last_link == (void **)&slot);

    return 0;
}
