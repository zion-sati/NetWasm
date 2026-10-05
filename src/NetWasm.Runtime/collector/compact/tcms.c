/* Copyright (c) The AssemblyScript Authors.
 * SPDX-License-Identifier: Apache-2.0
 * Modified by Zion Sati: C port of std/assembly/rt/tcms.ts at
 * b6bda05c29c9eb7d07d7f6cb2703508483b30493. See NOTICE for adaptations. */
#include "tcms.h"
#include <string.h>

union TcmsObject {
    struct {
        TcmsObject *next, *previous;
        size_t payload_bytes, allocation_bytes;
        TcmsLayout layout;
        size_t pins;
        bool colour;
    } data;
    max_align_t alignment;
};

static void *payload(TcmsObject *object) { return object + 1; }

static void append(TcmsList *list, TcmsObject *object)
{
    object->data.next = NULL;
    object->data.previous = list->last;
    if (list->last != NULL) list->last->data.next = object;
    else list->first = object;
    list->last = object;
}

static void unlink(TcmsList *list, TcmsObject *object)
{
    TcmsObject *next = object->data.next, *previous = object->data.previous;
    if (previous != NULL) previous->data.next = next;
    else list->first = next;
    if (next != NULL) next->data.previous = previous;
    else list->last = previous;
}

/* One pointer per allocation, O(log N) lookup, O(N) worst-case insertion.
 * Lookup never dereferences an unrecognized candidate address. */
static TcmsObject *resolve(const Tcms *gc, uintptr_t address, bool include_endpoint)
{
    size_t low = 0, high = gc->objects;
    while (low < high) {
        size_t middle = low + (high - low) / 2;
        if ((uintptr_t)payload(gc->index[middle]) <= address) low = middle + 1;
        else high = middle;
    }
    if (low == 0) return NULL;
    TcmsObject *object = gc->index[low - 1];
    uintptr_t distance = address - (uintptr_t)payload(object);
    return distance == 0 || distance < object->data.payload_bytes ||
        (include_endpoint && distance == object->data.payload_bytes) ? object : NULL;
}

bool tcms_mark(Tcms *gc, uintptr_t address)
{
    TcmsObject *object = resolve(gc, address, true);
    if (object != NULL && object->data.colour == gc->white) {
        object->data.colour = !gc->white;
        unlink(&gc->from_space, object);
        append(&gc->to_space, object);
        return true;
    }
    return false;
}

bool tcms_is_marked(const Tcms *gc, uintptr_t address)
{
    TcmsObject *object = resolve(gc, address, true);
    return object != NULL && object->data.colour != gc->white;
}

static TcmsResult grow_index(Tcms *gc)
{
    if (gc->objects < gc->index_capacity) return TCMS_OK;
    if (gc->index_capacity > SIZE_MAX / (2 * sizeof(TcmsObject *))) return TCMS_SIZE_OVERFLOW;
    size_t capacity = gc->index_capacity == 0 ? 4 : gc->index_capacity * 2;
    TcmsObject **index = gc->allocate(capacity * sizeof(*index), _Alignof(TcmsObject *));
    if (index == NULL) return TCMS_OUT_OF_MEMORY;
    if (gc->index != NULL) {
        memcpy(index, gc->index, gc->objects * sizeof(*index));
        gc->release(gc->index, gc->index_capacity * sizeof(*index), _Alignof(TcmsObject *));
    }
    gc->index = index;
    gc->index_capacity = capacity;
    return TCMS_OK;
}

static void release_white(Tcms *gc)
{
    TcmsObject *object = gc->from_space.first;
    while (object != NULL) {
        TcmsObject *next = object->data.next;
        size_t bytes = object->data.allocation_bytes;
        gc->bytes -= bytes;
        gc->objects--;
        gc->release(object, bytes, _Alignof(TcmsObject));
        object = next;
    }
    gc->from_space = (TcmsList){0};
}

TcmsResult tcms_initialize(Tcms *gc, TcmsAllocate allocate, TcmsRelease release, size_t threshold)
{
    if (gc == NULL || allocate == NULL || release == NULL) return TCMS_INVALID_ARGUMENT;
    *gc = (Tcms){.allocate = allocate, .release = release,
        .threshold = threshold, .minimum_threshold = threshold};
    return TCMS_OK;
}

static TcmsResult validate_layout(size_t bytes, TcmsLayout layout)
{
    if (layout.reference_count == 0) return TCMS_OK;
    if (layout.offsets == NULL || layout.stride == 0 ||
        layout.elements > SIZE_MAX / layout.stride ||
        layout.elements * layout.stride > bytes) return TCMS_INVALID_LAYOUT;
    for (size_t index = 0; index < layout.reference_count; index++) {
        size_t offset = layout.offsets[index];
        if (offset > SIZE_MAX - sizeof(uintptr_t) ||
            offset + sizeof(uintptr_t) > layout.stride) return TCMS_INVALID_LAYOUT;
    }
    return TCMS_OK;
}

TcmsResult tcms_allocate(Tcms *gc, size_t bytes, TcmsLayout layout,
    const uintptr_t *roots, size_t root_count, void **result)
{
    if (gc == NULL || result == NULL || (root_count != 0 && roots == NULL))
        return TCMS_INVALID_ARGUMENT;
    TcmsResult status = validate_layout(bytes, layout);
    if (status != TCMS_OK) return status;
    size_t payload_bytes = bytes == 0 ? 1 : bytes;
    if (payload_bytes > SIZE_MAX - sizeof(TcmsObject)) return TCMS_SIZE_OVERFLOW;
    size_t allocation_bytes = sizeof(TcmsObject) + payload_bytes;
    if (gc->bytes >= gc->threshold) tcms_collect(gc, roots, root_count);
    if (gc->bytes > SIZE_MAX - allocation_bytes || gc->objects == SIZE_MAX)
        return TCMS_SIZE_OVERFLOW;
    TcmsObject *object = gc->allocate(allocation_bytes, _Alignof(TcmsObject));
    if (object == NULL) return TCMS_OUT_OF_MEMORY;
    status = grow_index(gc);
    if (status != TCMS_OK) {
        gc->release(object, allocation_bytes, _Alignof(TcmsObject));
        return status;
    }
    object->data.payload_bytes = bytes;
    object->data.allocation_bytes = allocation_bytes;
    object->data.layout = layout;
    object->data.pins = 0;
    object->data.colour = gc->white;
    memset(payload(object), 0, payload_bytes);
    append(&gc->from_space, object);
    size_t low = 0, high = gc->objects;
    while (low < high) {
        size_t middle = low + (high - low) / 2;
        if ((uintptr_t)gc->index[middle] < (uintptr_t)object) low = middle + 1;
        else high = middle;
    }
    memmove(gc->index + low + 1, gc->index + low, (gc->objects - low) * sizeof(*gc->index));
    gc->index[low] = object;
    gc->bytes += allocation_bytes;
    gc->objects++;
    *result = payload(object);
    return TCMS_OK;
}

void tcms_collect(Tcms *gc, const uintptr_t *roots, size_t root_count)
{
    gc->collecting = true;
    gc->scan_cursor = NULL;
    for (size_t index = 0; index < root_count; index++) tcms_mark(gc, roots[index]);
    if (gc->trace_roots != NULL) gc->trace_roots(gc, gc->root_state);
    TcmsObject *pinned = gc->from_space.first;
    while (pinned != NULL) {
        TcmsObject *next = pinned->data.next;
        if (pinned->data.pins != 0) tcms_mark(gc, (uintptr_t)payload(pinned));
        pinned = next;
    }
    tcms_drain_mark(gc);
    if (gc->hooks != NULL) gc->hooks->complete_mark(gc);
    size_t live = 0;
    for (size_t index = 0; index < gc->objects; index++) {
        TcmsObject *object = gc->index[index];
        if (object->data.colour != gc->white) gc->index[live++] = object;
    }
    release_white(gc);
    gc->from_space = gc->to_space;
    gc->to_space = (TcmsList){0};
    gc->white = !gc->white;
    gc->collecting = false;
    gc->scan_cursor = NULL;
    gc->threshold = gc->bytes > (SIZE_MAX - gc->minimum_threshold) / 2
        ? SIZE_MAX : gc->bytes * 2 + gc->minimum_threshold;
}

void tcms_drain_mark(Tcms *gc)
{
    TcmsObject *object = gc->scan_cursor == NULL ? gc->to_space.first : gc->scan_cursor->data.next;
    while (object != NULL) {
        TcmsLayout layout = object->data.layout;
        for (size_t index = 0; index < layout.reference_count; index++) {
            for (size_t element = 0; element < layout.elements; element++) {
                uintptr_t reference;
                /* Packed reference fields need not be pointer-aligned. */
                memcpy(&reference, (unsigned char *)payload(object) +
                    element * layout.stride + layout.offsets[index], sizeof(reference));
                tcms_mark(gc, reference);
            }
        }
        /* Visiting appends new live objects. Read next after tracing members. */
        gc->scan_cursor = object;
        object = object->data.next;
    }
}

bool tcms_contains(const Tcms *gc, uintptr_t address)
{
    return resolve(gc, address, false) != NULL;
}

uintptr_t tcms_base(const Tcms *gc, uintptr_t address)
{
    TcmsObject *object = resolve(gc, address, false);
    return object == NULL ? 0 : (uintptr_t)payload(object);
}

TcmsResult tcms_pin(Tcms *gc, uintptr_t address)
{
    TcmsObject *object = resolve(gc, address, true);
    if (object == NULL) return TCMS_UNKNOWN_OBJECT;
    if (object->data.pins == SIZE_MAX) return TCMS_PIN_OVERFLOW;
    object->data.pins++;
    return TCMS_OK;
}

TcmsResult tcms_unpin(Tcms *gc, uintptr_t address)
{
    TcmsObject *object = resolve(gc, address, true);
    if (object == NULL) return TCMS_UNKNOWN_OBJECT;
    if (object->data.pins == 0) return TCMS_NOT_PINNED;
    object->data.pins--;
    return TCMS_OK;
}

void tcms_destroy(Tcms *gc)
{
    if (gc->hooks != NULL) gc->hooks->dispose(gc);
    release_white(gc);
    if (gc->index != NULL)
        gc->release(gc->index, gc->index_capacity * sizeof(*gc->index), _Alignof(TcmsObject *));
    gc->index = NULL;
    gc->index_capacity = 0;
}
