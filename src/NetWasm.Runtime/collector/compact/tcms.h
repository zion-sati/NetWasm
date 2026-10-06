/* Copyright (c) The AssemblyScript Authors.
 * SPDX-License-Identifier: Apache-2.0
 * Modified by Zion Sati: C port of std/assembly/rt/tcms.ts at
 * b6bda05c29c9eb7d07d7f6cb2703508483b30493. See NOTICE for adaptations. */
#ifndef NETWASM_TCMS_H
#define NETWASM_TCMS_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef struct {
    const size_t *offsets;
    size_t reference_count;
    size_t stride;
    size_t elements;
} TcmsLayout;

typedef void *(*TcmsAllocate)(size_t bytes, size_t alignment);
typedef void (*TcmsRelease)(void *address, size_t bytes, size_t alignment);
typedef union TcmsObject TcmsObject;
typedef struct { TcmsObject *first, *last; } TcmsList;
typedef struct Tcms Tcms;
typedef struct {
    void (*complete_mark)(Tcms *);
    void (*dispose)(Tcms *);
} TcmsHooks;
struct Tcms {
    TcmsList from_space, to_space;
    TcmsAllocate allocate;
    TcmsRelease release;
    size_t objects, bytes, threshold, minimum_threshold;
    TcmsObject **index;
    size_t index_capacity;
    TcmsObject *scan_cursor;
    const TcmsHooks *hooks;
    void *extension;
    void (*trace_roots)(Tcms *, void *);
    void *root_state;
    bool white, collecting;
};

typedef enum {
    TCMS_OK, TCMS_INVALID_ARGUMENT, TCMS_INVALID_LAYOUT, TCMS_SIZE_OVERFLOW,
    TCMS_OUT_OF_MEMORY, TCMS_UNKNOWN_OBJECT, TCMS_PIN_OVERFLOW, TCMS_NOT_PINNED
} TcmsResult;

/* Single-threaded and non-reentrant. Allocator must honor alignment and own
 * disjoint live blocks. Layout offsets must outlive their objects. Roots and
 * payload reference slots must be initialized addresses or zero. No feature
 * here supplies finalization/weak/ephemeron semantics yet. */
TcmsResult tcms_initialize(Tcms *, TcmsAllocate, TcmsRelease, size_t threshold);
TcmsResult tcms_allocate(Tcms *, size_t bytes, TcmsLayout,
    const uintptr_t *roots, size_t root_count, void **result);
void tcms_collect(Tcms *, const uintptr_t *roots, size_t root_count);
/* Only exact known managed root/field addresses may be passed to mark. Interior
 * and logical-endpoint byrefs retain their owner; no arbitrary payload scan. */
bool tcms_mark(Tcms *, uintptr_t address);
void tcms_drain_mark(Tcms *);
bool tcms_is_marked(const Tcms *, uintptr_t address);
uintptr_t tcms_base(const Tcms *, uintptr_t address);
bool tcms_contains(const Tcms *, uintptr_t address);
TcmsResult tcms_pin(Tcms *, uintptr_t address);
TcmsResult tcms_unpin(Tcms *, uintptr_t address);
void tcms_destroy(Tcms *);

#endif
