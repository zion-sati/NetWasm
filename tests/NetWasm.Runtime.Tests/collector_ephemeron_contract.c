#include "collector_ephemeron.h"
#include "collector_metadata.h"
#include "collector_weak_reference.h"

#include <gc.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

typedef struct Object {
    struct Object *child;
    int marked;
} Object;

static GC_on_collection_event_proc collection_event;
static Object *mark_stack[8];
static size_t mark_stack_count;
static unsigned previous_event_count;
static unsigned weak_registration_count;
static unsigned weak_unregistration_count;

static void require(int condition, const char *message)
{
    if (condition) return;
    fprintf(stderr, "%s\n", message);
    exit(1);
}

void *collector_allocate_metadata(size_t size)
{
    return calloc(1, size);
}

void collector_release_metadata(void *address)
{
    free(address);
}

int collector_register_short_weak_reference(uintptr_t *slot, uintptr_t target)
{
    require(slot != NULL && target != 0, "invalid weak registration");
    weak_registration_count++;
    return 1;
}

int collector_unregister_weak_reference(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime lifetime)
{
    require(slot != NULL, "invalid weak unregistration");
    require(
        lifetime == NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION,
        "ephemeron key must use a short weak reference");
    weak_unregistration_count++;
    return 1;
}

GC_on_collection_event_proc GC_get_on_collection_event(void)
{
    return collection_event;
}

void GC_set_on_collection_event(GC_on_collection_event_proc callback)
{
    collection_event = callback;
}

int GC_is_marked(const void *address)
{
    return ((const Object *)address)->marked;
}

void GC_push_all_eager(void *bottom, void *top)
{
    uintptr_t *slot = bottom;
    require(slot + 1 == (uintptr_t *)top, "expected one ephemeron value slot");
    Object *object = (Object *)*slot;
    if (object == NULL || object->marked) return;
    object->marked = 1;
    require(mark_stack_count < 8, "fake mark stack overflow");
    mark_stack[mark_stack_count++] = object;
}

int GC_mark_stack_empty(void)
{
    return mark_stack_count == 0;
}

void fake_mark_from_mark_stack(void)
{
    require(mark_stack_count != 0, "fake mark stack underflow");
    Object *object = mark_stack[--mark_stack_count];
    if (object->child == NULL || object->child->marked) return;
    object->child->marked = 1;
    require(mark_stack_count < 8, "fake mark stack overflow");
    mark_stack[mark_stack_count++] = object->child;
}

static void previous_event(GC_EventType event)
{
    (void)event;
    previous_event_count++;
}

int main(void)
{
    Object key1 = {0};
    Object value1 = {0};
    Object key2 = {0};
    Object value2 = {0};
    Object deadKey = {0};
    Object deadValue = {0};
    uintptr_t key1Slot = (uintptr_t)&key1;
    uintptr_t value1Slot = (uintptr_t)&value1;
    uintptr_t key2Slot = (uintptr_t)&key2;
    uintptr_t value2Slot = (uintptr_t)&value2;
    uintptr_t deadKeySlot = (uintptr_t)&deadKey;
    uintptr_t deadValueSlot = (uintptr_t)&deadValue;

    value1.child = &key2;
    deadValue.child = &deadKey;
    collection_event = previous_event;

    require(
        collector_register_ephemeron(&key2Slot, &value2Slot),
        "second ephemeron registration failed");
    require(
        collector_register_ephemeron(&key1Slot, &value1Slot),
        "first ephemeron registration failed");
    require(
        collector_register_ephemeron(&deadKeySlot, &deadValueSlot),
        "dead ephemeron registration failed");
    require(weak_registration_count == 3, "weak registrations were not forwarded");

    key1.marked = 1;
    collection_event(GC_EVENT_START);
    require(previous_event_count == 1, "previous event callback was not preserved");
    require(!value1.marked && !key2.marked && !value2.marked,
        "non-mark-end event changed ephemeron reachability");

    collection_event(GC_EVENT_MARK_END);
    require(previous_event_count == 2, "previous mark-end callback was not preserved");
    require(value1.marked, "live key did not retain its value");
    require(key2.marked, "ephemeron value did not trace its child");
    require(value2.marked, "reversed ephemeron chain did not reach a fixed point");
    require(!deadKey.marked && !deadValue.marked,
        "unreachable value-to-key cycle became reachable");

    collector_unregister_ephemeron(&key1Slot, &value1Slot);
    collector_unregister_ephemeron(&key2Slot, &value2Slot);
    deadKeySlot = 0;
    collector_unregister_ephemeron(&deadKeySlot, &deadValueSlot);
    require(weak_unregistration_count == 2,
        "cleared weak keys must not be unregistered twice");

    puts("Collector ephemeron contract PASS");
    return 0;
}
