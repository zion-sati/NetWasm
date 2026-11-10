#ifndef NETWASM_COLLECTOR_ALLOCATION_H
#define NETWASM_COLLECTOR_ALLOCATION_H

#include <stdalign.h>
#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>

/* A collected object's layout, read from its type id: references at
 * fixed_offsets within its first fixed_bytes, then element_count elements of
 * element_size bytes with references at element_offsets. Collectors keep no
 * per-object layout. */
typedef struct {
    const size_t *fixed_offsets;
    size_t fixed_reference_count;
    size_t fixed_bytes;
    const size_t *element_offsets;
    size_t element_reference_count;
    size_t element_size;
    size_t element_count;
} NetWasmCollectorLayout;

/* Implemented by the runtime. Valid once the object's type id, lengths and
 * data pointer are stored; it does not allocate. */
void collector_object_layout(const void *object, NetWasmCollectorLayout *layout);

/* Returns zeroed storage. The collector traces it through
 * collector_object_layout only when contains_references is true. */
void *collector_allocate_object(size_t bytes, bool contains_references);

/* Allocation runs: the contract that lets the runtime allocate a small object
 * without calling the collector. An object of `bytes`, below
 * NETWASM_ALLOCATION_RUN_LIMIT, takes a slot of NETWASM_ALLOCATION_SLOT(bytes)
 * bytes from the run at collector_allocation_runs[class][contains_references],
 * class being slot / NETWASM_ALLOCATION_ALIGNMENT - 1: when cursor + slot <= end,
 * the object is at cursor, its storage is zeroed, the cursor advances by slot
 * and the caller stores the object's type id; otherwise it calls the allocator.
 * A collector without runs leaves every cursor equal to its end (both null).
 * The alignment is max_align_t's: 8 bytes on the Wasm targets. */
typedef struct {
    unsigned char *cursor;
    unsigned char *end;
} NetWasmAllocationRun;

/* Whether this build's collector keeps runs: NetWasmGC does, except under
 * stress, where only its allocator collects before every allocation. */
#if defined(NETWASM_GC_COMPACT) || defined(NETWASM_GC_STRESS) || defined(NETWASM_BUMP_COLLECTOR)
#define NETWASM_ALLOCATION_RUNS 0
#else
#define NETWASM_ALLOCATION_RUNS 1
#endif

/* A string or array allocator's slow path stays out of line where a run fast
 * path is inlined in front of it; without runs there is no fast path. */
#if NETWASM_ALLOCATION_RUNS
#define NETWASM_ALLOCATION_SLOW_PATH __attribute__((noinline))
#else
#define NETWASM_ALLOCATION_SLOW_PATH
#endif

#define NETWASM_ALLOCATION_ALIGNMENT alignof(max_align_t)
#define NETWASM_ALLOCATION_RUN_LIMIT 256u
#define NETWASM_ALLOCATION_RUN_CLASSES (NETWASM_ALLOCATION_RUN_LIMIT / NETWASM_ALLOCATION_ALIGNMENT)
#define NETWASM_ALLOCATION_SLOT(bytes) \
    (((bytes) + NETWASM_ALLOCATION_ALIGNMENT) & ~(size_t)(NETWASM_ALLOCATION_ALIGNMENT - 1))

/* [NETWASM_ALLOCATION_RUN_CLASSES][2] runs at least; the collector may keep
 * more classes after them. */
extern NetWasmAllocationRun *collector_allocation_runs;

#endif
