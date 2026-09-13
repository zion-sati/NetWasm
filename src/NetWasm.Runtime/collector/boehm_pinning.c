#include "collector_pinning.h"

static uint64_t pinned_reference_count;

void collector_pin_reference(uintptr_t *slot)
{
    (void)slot;
    pinned_reference_count++;
}

void collector_unpin_reference(uintptr_t *slot)
{
    (void)slot;
    if (pinned_reference_count != 0) pinned_reference_count--;
}

uint64_t collector_pinned_reference_count(void)
{
    return pinned_reference_count;
}
