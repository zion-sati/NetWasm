#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "native_target.h"
#include "gc_metric_reader.h"
#include "runtime_system_initializer.h"
#include "object_data_address_resolver.h"
#include "unmanaged_allocator.h"
#include "wasi_random_import.h"
#include "collector/weak_handle_table.h"
#include "collector/strong_handle_table.h"
#include "collector/gc_handle_table.h"
#include "collector/collector_allocation.h"
#include "collector/collector_collection.h"
#include "collector/collector_finalization.h"
#include "collector/collector_identity_hash.h"
#include "collector/collector_lifecycle.h"
#include "collector/collector_metadata.h"
#include "collector/collector_pinning.h"
#include "collector/collector_reference_store.h"
#include "collector/collector_roots.h"
#include "collector/collector_weak_reference.h"

#ifdef NETWASM_RUNTIME_PACK
#define NETWASM_EXPORT(name)
#define NETWASM_TEST_EXPORT(name)
#else
#define NETWASM_EXPORT(name) __attribute__((export_name(name)))
#define NETWASM_TEST_EXPORT(name) __attribute__((export_name(name)))
#endif
#if defined(NETWASM_RUNTIME_PACK) || defined(NETWASM_NO_FINALIZATION)
#define NETWASM_FINALIZATION_EXPORT(name)
#else
#define NETWASM_FINALIZATION_EXPORT(name) __attribute__((export_name(name)))
#endif

typedef uint32_t u32;

NETWASM_EXPORT("random_seed") uint64_t random_seed(void)
{
    return runtime_get_random_u64();
}

_Static_assert(sizeof(netwasm_id_t) == sizeof(u32),
               "NetWasm semantic identifiers must remain i32");
_Static_assert(sizeof(netwasm_reference_t) == sizeof(void *),
               "A managed reference must match the selected Wasm address width");

/* The compiler emits each type's reference bitmap as words of this size. */
typedef uintptr_t NetWasmReferenceBitmapWord;

typedef struct {
    u32 base_type_id;
    netwasm_address_t size;
    netwasm_address_t bitmap_address;
    u32 bitmap_bits;
    netwasm_address_t assignable_type_ids_address;
    u32 assignable_type_id_count;
    u32 has_finalizer;
    u32 is_interface;
    netwasm_address_t value_size;
    netwasm_address_t boxed_payload_offset;
    int value_contains_references;
    int value_registered;
    int registered;
    const size_t *reference_offsets;
    size_t reference_count;
    const size_t *value_reference_offsets;
    size_t value_reference_count;
} TypeDescriptor;

/* The frame arenas as they stood at a point managed code may later catch an
   exception. Methods do not release their frames when an exception passes
   through them; whatever catches it restores these marks instead. */
typedef struct {
    uintptr_t root;
    struct ValueFrame *value;
    u32 stack_trace;
} FrameMarks;

static FrameMarks frame_marks_capture(void);
static void frame_marks_restore(const FrameMarks *marks);

typedef struct {
    netwasm_address_t metadata_address;
    u32 clause_count;
    netwasm_address_t environment;
    u32 has_filters;
    /* Search selects before unwind. Inner handled throws must not overwrite
       the clause already selected on an enclosing frame. */
    u32 target_clause;
    FrameMarks marks;
} ExceptionFrame;

typedef struct {
    u32 kind;
    u32 value;
    u32 reserved;
} ExceptionClause;

typedef struct {
    uint16_t *characters;
    u32 length;
} StackTraceSymbol;

typedef struct {
    u32 method_id;
    u32 symbol_id;
} StackTraceFrame;

_Static_assert(sizeof(ExceptionClause) == 12,
               "Exception-clause metadata must remain three u32 fields");

typedef struct FrameArenaChunk {
    struct FrameArenaChunk *next;
    /* Root frames are LIFO; ownership changes only when crossing chunks. */
    struct FrameArenaChunk *previous;
    uintptr_t saved_cursor;
    size_t capacity;
    unsigned char bytes[];
} FrameArenaChunk;

typedef struct {
    FrameArenaChunk *current;
    uintptr_t cursor, limit, begin;
} FrameArena;

static inline __attribute__((always_inline)) unsigned char *frame_arena_bytes(
    FrameArenaChunk *chunk);

typedef struct ValueFrame {
    struct ValueFrame *previous;
    FrameArenaChunk *chunk;
    _Alignas(16) unsigned char bytes[];
} ValueFrame;

_Static_assert(sizeof(ValueFrame) % 16u == 0,
               "Value-frame headers must preserve arena alignment");

typedef struct {
    void *allocation;
    netwasm_address_t size;
    netwasm_address_t alignment;
} ComponentAllocationHeader;

static TypeDescriptor *type_descriptors;
static uint8_t *type_data_kinds;
static u32 type_capacity;
static netwasm_address_t *static_roots;
static u32 static_root_capacity;
static u32 static_root_count;
static FrameArena root_frame_arena;
static ValueFrame *value_frame;
static FrameArena value_frame_arena;
static StackTraceFrame *stack_trace_frames;
static u32 stack_trace_frame_capacity;
static u32 stack_trace_frame_top;
static StackTraceSymbol *stack_trace_symbols;
static u32 stack_trace_symbol_capacity;
static u32 stack_trace_exception_field_offset;
static u32 stack_trace_string_type_id;
static u32 stack_trace_initialized;
static ExceptionFrame *exception_frames;
static u32 exception_frame_capacity;
static u32 exception_frame_top;
static u32 filter_search_floor;
#ifndef NETWASM_RUNTIME_PACK
static uintptr_t *tracked_allocations;
static u32 tracked_capacity;
static u32 tracked_count;
static u32 allocation_count;
static u32 collection_count;
static u32 collect_before_allocation;
static u32 track_allocations;
static u32 fail_next_allocation;
static u32 component_active_allocation_count;
static u32 fail_next_component_allocation;
#endif
static netwasm_reference_t dispatch_root;
static uintptr_t allocation_root;

netwasm_reference_t allocate_string(u32 value, u32 length, u32 type_id);
static netwasm_reference_t *type_object_cache;

__attribute__((import_module("netwasm.application.v1"),
               import_name("netwasm.filter")))
u32 netwasm_evaluate_filter(
    u32 funclet_id,
    netwasm_reference_t exception,
    netwasm_address_t environment);
#ifndef NETWASM_NO_FINALIZATION
static uintptr_t finalizer_root;
static u32 finalizer_count;
static u32 escaped_finalizer_status;
static u32 draining_finalizers;
static int finalization_initialized;

__attribute__((import_module("netwasm.application.v1"),
               import_name("netwasm.finalize")))
u32 netwasm_managed_finalize(netwasm_reference_t object, u32 type_id);

static void managed_finalizer(void *raw_object, void *client_data)
{
    netwasm_reference_t object = (netwasm_reference_t)(uintptr_t)raw_object;
    u32 type_id = (u32)(uintptr_t)client_data;
    u32 status;
    finalizer_root = (uintptr_t)raw_object;
    if (escaped_finalizer_status != 0) {
        collector_register_finalizer(
            raw_object,
            managed_finalizer,
            client_data,
            NULL,
            NULL);
        finalizer_root = 0;
        return;
    }
    FrameMarks marks = frame_marks_capture();
    status = netwasm_managed_finalize(object, type_id);
    frame_marks_restore(&marks);
    finalizer_count++;
    if (status != 0 && escaped_finalizer_status == 0) {
        escaped_finalizer_status = status;
    }
    finalizer_root = 0;
}
#endif
static int initialized;


static void write_diagnostic_bytes(const char *value, size_t length)
{
    while (length != 0) {
        ssize_t written = write(STDERR_FILENO, value, length);
        if (written <= 0) {
            return;
        }
        value += written;
        length -= (size_t)written;
    }
}

#ifdef NETWASM_STRUCTURED_COMMAND_DIAGNOSTICS
#include "command_diagnostics.h"
#endif

NETWASM_EXPORT("report_unobserved_task_exception")
void report_unobserved_task_exception(void)
{
    /* Naming the exception would bring type-name and message formatting into
     * every asynchronous application, so the handler of the standard event
     * receives it instead. */
    static const char line[] =
        "NetWasm: an unobserved managed Task exception was finalized\n";
    write_diagnostic_bytes(line, sizeof(line) - 1);
}

static int grow_exception_frames(void)
{
    u32 capacity = exception_frame_capacity == 0 ? 16 :
        exception_frame_capacity * 2;
    ExceptionFrame *frames;
    if (capacity < exception_frame_capacity ||
        (size_t)capacity > SIZE_MAX / sizeof(ExceptionFrame)) {
        return 0;
    }
    frames = (ExceptionFrame *)collector_allocate_metadata(
        (size_t)capacity * sizeof(ExceptionFrame));
    if (frames == NULL) {
        return 0;
    }
    memset(frames, 0, (size_t)capacity * sizeof(ExceptionFrame));
    if (exception_frame_capacity != 0) {
        memcpy(frames, exception_frames,
               (size_t)exception_frame_capacity * sizeof(ExceptionFrame));
        collector_release_metadata(exception_frames);
    }
    exception_frames = frames;
    exception_frame_capacity = capacity;
    return 1;
}

static int grow_stack_trace_frames(void)
{
    u32 capacity = stack_trace_frame_capacity == 0 ? 32 :
        stack_trace_frame_capacity * 2;
    StackTraceFrame *frames;
    if (capacity < stack_trace_frame_capacity ||
        (size_t)capacity > SIZE_MAX / sizeof(StackTraceFrame)) {
        return 0;
    }
    frames = (StackTraceFrame *)collector_allocate_metadata(
        (size_t)capacity * sizeof(StackTraceFrame));
    if (frames == NULL) {
        return 0;
    }
    if (stack_trace_frame_top != 0) {
        memcpy(frames, stack_trace_frames,
               (size_t)stack_trace_frame_top * sizeof(StackTraceFrame));
    }
    if (stack_trace_frame_capacity != 0) {
        collector_release_metadata(stack_trace_frames);
    }
    stack_trace_frames = frames;
    stack_trace_frame_capacity = capacity;
    return 1;
}

NETWASM_EXPORT("stack_trace_frame_enter")
void stack_trace_frame_enter(u32 method_id)
{
    if (method_id == 0 ||
        (stack_trace_frame_top == stack_trace_frame_capacity &&
         !grow_stack_trace_frames())) {
        __builtin_trap();
    }
    stack_trace_frames[stack_trace_frame_top++] =
        (StackTraceFrame){ method_id, method_id };
}

NETWASM_EXPORT("stack_trace_frame_location")
void stack_trace_frame_location(u32 method_id, u32 symbol_id)
{
    if (method_id == 0 || symbol_id == 0 || stack_trace_frame_top == 0 ||
        stack_trace_frames[stack_trace_frame_top - 1].method_id != method_id) {
        __builtin_trap();
    }
    stack_trace_frames[stack_trace_frame_top - 1].symbol_id = symbol_id;
}

NETWASM_EXPORT("stack_trace_frame_leave")
void stack_trace_frame_leave(u32 method_id)
{
    if (method_id == 0 || stack_trace_frame_top == 0 ||
        stack_trace_frames[stack_trace_frame_top - 1].method_id != method_id) {
        __builtin_trap();
    }
    stack_trace_frame_top--;
}

static int grow_stack_trace_symbols(u32 method_id)
{
    u32 capacity = stack_trace_symbol_capacity == 0 ? 32 :
        stack_trace_symbol_capacity;
    while (capacity <= method_id) {
        if (capacity > UINT32_MAX / 2) {
            return 0;
        }
        capacity *= 2;
    }
    if ((size_t)capacity > SIZE_MAX / sizeof(StackTraceSymbol)) {
        return 0;
    }
    StackTraceSymbol *symbols = collector_allocate_metadata(
        (size_t)capacity * sizeof(StackTraceSymbol));
    if (symbols == NULL) {
        return 0;
    }
    memset(symbols, 0, (size_t)capacity * sizeof(StackTraceSymbol));
    if (stack_trace_symbol_capacity != 0) {
        memcpy(symbols, stack_trace_symbols,
            (size_t)stack_trace_symbol_capacity * sizeof(StackTraceSymbol));
        collector_release_metadata(stack_trace_symbols);
    }
    stack_trace_symbols = symbols;
    stack_trace_symbol_capacity = capacity;
    return 1;
}

NETWASM_EXPORT("stack_trace_initialize")
void stack_trace_initialize(u32 exception_field_offset, u32 string_type_id)
{
    if (exception_field_offset == 0 || string_type_id == 0) {
        __builtin_trap();
    }
    if (stack_trace_initialized &&
        (stack_trace_exception_field_offset != exception_field_offset ||
         stack_trace_string_type_id != string_type_id)) {
        __builtin_trap();
    }
    stack_trace_exception_field_offset = exception_field_offset;
    stack_trace_string_type_id = string_type_id;
    stack_trace_initialized = 1;
}

NETWASM_EXPORT("stack_trace_register_symbol")
void stack_trace_register_symbol(
    u32 method_id,
    netwasm_address_t characters,
    u32 length)
{
    if (method_id == 0) {
        __builtin_trap();
    }
    if (method_id < stack_trace_symbol_capacity &&
        stack_trace_symbols[method_id].characters != NULL) {
        return;
    }
    if (characters == 0 || length == 0 ||
        (method_id >= stack_trace_symbol_capacity &&
         !grow_stack_trace_symbols(method_id)) ||
        (size_t)length > SIZE_MAX / sizeof(uint16_t)) {
        __builtin_trap();
    }
    uint16_t *copy = collector_allocate_metadata(
        (size_t)length * sizeof(uint16_t));
    if (copy == NULL) {
        __builtin_trap();
    }
    memcpy(copy, (const void *)(uintptr_t)characters,
        (size_t)length * sizeof(uint16_t));
    stack_trace_symbols[method_id].characters = copy;
    stack_trace_symbols[method_id].length = length;
}

static u32 decimal_digit_count(u32 value)
{
    u32 count = 1;
    while (value >= 10) {
        value /= 10;
        count++;
    }
    return count;
}

static uint16_t *write_decimal(uint16_t *destination, u32 value)
{
    u32 count = decimal_digit_count(value);
    uint16_t *end = destination + count;
    do {
        *--end = (uint16_t)('0' + value % 10);
        value /= 10;
    } while (value != 0);
    return destination + count;
}

static int add_trace_length(u32 *total, u32 addition)
{
    if (addition > UINT32_MAX - *total) {
        return 0;
    }
    *total += addition;
    return 1;
}

static void capture_stack_trace(netwasm_reference_t exception)
{
    if (!stack_trace_initialized || exception == 0 ||
        stack_trace_frame_top == 0) {
        return;
    }
    u32 length = 0;
    for (u32 index = stack_trace_frame_top; index != 0; index--) {
        StackTraceFrame frame = stack_trace_frames[index - 1];
        StackTraceSymbol *symbol = frame.symbol_id < stack_trace_symbol_capacity ?
            &stack_trace_symbols[frame.symbol_id] : NULL;
        u32 frame_length = symbol != NULL && symbol->characters != NULL ?
            symbol->length : 7 + decimal_digit_count(frame.symbol_id);
        if (!add_trace_length(&length, 3) ||
            !add_trace_length(&length, frame_length) ||
            (index != 1 && !add_trace_length(&length, 1))) {
            return;
        }
    }

    collector_register_roots(&exception, sizeof(exception));
    netwasm_reference_t trace = allocate_string(
        0,
        length,
        stack_trace_string_type_id);
    if (trace == 0) {
        collector_unregister_roots(&exception, sizeof(exception));
        return;
    }
    uint16_t *destination = (uint16_t *)(uintptr_t)(
        trace + NETWASM_STRING_DATA_OFFSET);
    for (u32 index = stack_trace_frame_top; index != 0; index--) {
        *destination++ = 'a';
        *destination++ = 't';
        *destination++ = ' ';
        StackTraceFrame frame = stack_trace_frames[index - 1];
        StackTraceSymbol *symbol = frame.symbol_id < stack_trace_symbol_capacity ?
            &stack_trace_symbols[frame.symbol_id] : NULL;
        if (symbol != NULL && symbol->characters != NULL) {
            memcpy(destination, symbol->characters,
                (size_t)symbol->length * sizeof(uint16_t));
            destination += symbol->length;
        } else {
            const char fallback[] = "method#";
            for (u32 character = 0; character < 7; character++) {
                *destination++ = (uint16_t)fallback[character];
            }
            destination = write_decimal(destination, frame.symbol_id);
        }
        if (index != 1) {
            *destination++ = '\n';
        }
    }
    netwasm_reference_t *slot = (netwasm_reference_t *)(uintptr_t)(
        exception + stack_trace_exception_field_offset);
    collector_store_reference(exception, slot, trace);
    collector_unregister_roots(&exception, sizeof(exception));
}

static void capture_stack_trace_if_missing(netwasm_reference_t exception)
{
    if (!stack_trace_initialized || exception == 0) {
        return;
    }
    netwasm_reference_t *slot = (netwasm_reference_t *)(uintptr_t)(
        exception + stack_trace_exception_field_offset);
    if (*slot == 0) {
        capture_stack_trace(exception);
    }
}


static void collect_internal(void)
{
    collector_collect();
#ifndef NETWASM_RUNTIME_PACK
    collection_count++;
#endif
}

NETWASM_EXPORT("collect")
void collect(void)
{
    collect_internal();
}

static inline __attribute__((always_inline))
void collect_for_allocation_test(void)
{
#ifndef NETWASM_RUNTIME_PACK
    if (collect_before_allocation) {
        collect_internal();
    }
#endif
}

#ifndef NETWASM_RUNTIME_PACK
static __attribute__((noinline, cold))
void track_enabled(void *allocation)
{
    uintptr_t address = (uintptr_t)allocation;
    uintptr_t saved_root;
    for (u32 index = 0; index < tracked_count; index++) {
        if (tracked_allocations[index] == address) {
            return;
        }
    }
    if (tracked_count == tracked_capacity) {
        u32 capacity = tracked_capacity == 0 ? 16 : tracked_capacity * 2;
        uintptr_t *allocations;
        if (capacity < tracked_capacity ||
            (size_t)capacity > SIZE_MAX / sizeof(uintptr_t)) {
            return;
        }
        saved_root = allocation_root;
        allocation_root = address;
        allocations = (uintptr_t *)collector_allocate_metadata(
            (size_t)capacity * sizeof(uintptr_t));
        allocation_root = saved_root;
        if (allocations == NULL) {
            return;
        }
        if (tracked_count != 0) {
            memcpy(allocations, tracked_allocations,
                   (size_t)tracked_count * sizeof(uintptr_t));
            for (u32 index = 0; index < tracked_count; index++) {
                if (tracked_allocations[index] != 0) {
                    collector_unregister_weak_reference(
                        &tracked_allocations[index],
                        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);
                }
            }
            collector_release_metadata(tracked_allocations);
        }
        tracked_allocations = allocations;
        tracked_capacity = capacity;
        for (u32 index = 0; index < tracked_count; index++) {
            if (tracked_allocations[index] != 0 &&
                collector_register_short_weak_reference(
                    &tracked_allocations[index],
                    tracked_allocations[index]) == 0) {
                tracked_allocations[index] = 0;
            }
        }
    }
    tracked_allocations[tracked_count] = address;
    if (collector_register_short_weak_reference(
            &tracked_allocations[tracked_count], address) == 0) {
        tracked_allocations[tracked_count] = 0;
    }
    tracked_count++;
}

static inline __attribute__((always_inline))
void track(void *allocation)
{
    if (track_allocations) {
        track_enabled(allocation);
    }
}
#else
static inline __attribute__((always_inline))
void track(void *allocation)
{
    (void)allocation;
}
#endif

static void enumerate_managed_roots(void)
{
    collector_trace_root_range(&dispatch_root, &dispatch_root + 1);
    collector_trace_root_range(&allocation_root, &allocation_root + 1);
#ifndef NETWASM_NO_FINALIZATION
    collector_trace_root_range(&finalizer_root, &finalizer_root + 1);
#endif
    for (u32 index = 0; index < static_root_count; index++) {
        netwasm_reference_t *slot =
            (netwasm_reference_t *)(uintptr_t)static_roots[index];
        collector_trace_root_range(slot, slot + 1);
    }
    uintptr_t end = root_frame_arena.cursor;
    for (FrameArenaChunk *chunk = root_frame_arena.current; chunk != NULL; chunk = chunk->previous) {
        collector_trace_root_range(frame_arena_bytes(chunk), (void *)end);
        if (chunk->previous != NULL) end = chunk->previous->saved_cursor;
    }
}

NETWASM_EXPORT("initialize")
void initialize(
    netwasm_address_t static_data_end,
    u32 required_type_capacity,
    u32 required_static_root_capacity)
{
    (void)static_data_end;
    if (initialized) {
        return;
    }

    /*
     * The compiler publishes only semantically live managed references and
     * managed byrefs in exact shadow-root slots. A managed byref can point into
     * an object or array, so the collector must resolve that interior address
     * back to its containing allocation. This does not enable conventional stack
     * scanning; the registered shadow/static/runtime roots remain the complete
     * root set.
     */
    if (required_type_capacity == 0 ||
        (size_t)required_type_capacity > SIZE_MAX / sizeof(TypeDescriptor) ||
        (size_t)required_type_capacity > SIZE_MAX / sizeof(netwasm_reference_t) ||
        (size_t)required_static_root_capacity >
            SIZE_MAX / sizeof(netwasm_address_t)) {
        __builtin_trap();
    }
    runtime_initialize_system();
    collector_initialize();
    collector_clear_roots();
    type_descriptors = (TypeDescriptor *)collector_allocate_metadata(
        (size_t)required_type_capacity * sizeof(TypeDescriptor));
    type_data_kinds = collector_allocate_metadata(required_type_capacity * sizeof(uint8_t));
    type_object_cache = (netwasm_reference_t *)collector_allocate_metadata(
        (size_t)required_type_capacity * sizeof(netwasm_reference_t));
    if (required_static_root_capacity != 0) {
        static_roots = (netwasm_address_t *)collector_allocate_metadata(
            (size_t)required_static_root_capacity * sizeof(netwasm_address_t));
    }
    if (type_descriptors == NULL || type_data_kinds == NULL || type_object_cache == NULL ||
        (required_static_root_capacity != 0 && static_roots == NULL)) {
        __builtin_trap();
    }
    memset(type_descriptors, 0,
           (size_t)required_type_capacity * sizeof(TypeDescriptor));
    memset(type_data_kinds, 0, required_type_capacity * sizeof(uint8_t));
    memset(type_object_cache, 0,
           (size_t)required_type_capacity * sizeof(netwasm_reference_t));
    if (required_static_root_capacity != 0) {
        memset(static_roots, 0,
               (size_t)required_static_root_capacity * sizeof(netwasm_address_t));
    }
    type_capacity = required_type_capacity;
    static_root_capacity = required_static_root_capacity;
    collector_set_root_enumerator(enumerate_managed_roots);
    initialized = 1;
}

NETWASM_FINALIZATION_EXPORT("initialize_finalization")
void initialize_finalization(void)
{
#ifndef NETWASM_NO_FINALIZATION
    if (!initialized) {
        __builtin_trap();
    }
    if (finalization_initialized) {
        return;
    }
    finalization_initialized = 1;
#endif
}

static u32 is_type_assignable(u32 actual_type_id, u32 target_type_id)
{
    u32 index;
    if (actual_type_id == 0 || target_type_id == 0) {
        return 0;
    }
    while (actual_type_id != 0) {
        if (actual_type_id == target_type_id) {
            return 1;
        }
        if (actual_type_id >= type_capacity ||
            !type_descriptors[actual_type_id].registered) {
            return 0;
        }
        for (index = 0;
             index < type_descriptors[actual_type_id].assignable_type_id_count;
             index++) {
            const u32 *assignable_type_ids = (const u32 *)(uintptr_t)
                type_descriptors[actual_type_id].assignable_type_ids_address;
            if (assignable_type_ids[index] == target_type_id) {
                return 1;
            }
        }
        actual_type_id = type_descriptors[actual_type_id].base_type_id;
    }
    return 0;
}

NETWASM_EXPORT("is_assignable")
u32 is_assignable(netwasm_reference_t object, u32 target_type_id)
{
    return object == 0
        ? 0
        : is_type_assignable(*(u32 *)(uintptr_t)object, target_type_id);
}

NETWASM_EXPORT("exception_frame_enter")
u32 exception_frame_enter(netwasm_address_t metadata_address, u32 clause_count)
{
    ExceptionFrame *frame;
    if ((clause_count != 0 && metadata_address == 0) ||
        (exception_frame_top == exception_frame_capacity &&
         !grow_exception_frames())) {
        __builtin_trap();
    }
    frame = &exception_frames[exception_frame_top++];
    frame->metadata_address = metadata_address;
    frame->has_filters = clause_count >> 31;
    frame->clause_count = clause_count & 0x7fffffffu;
    frame->environment = 0;
    frame->target_clause = 0;
    frame->marks = frame_marks_capture();
    return exception_frame_top;
}

NETWASM_EXPORT("exception_frame_set_environment")
void exception_frame_set_environment(u32 token, netwasm_address_t environment)
{
    if (token == 0 || token > exception_frame_top ||
        !exception_frames[token - 1].has_filters) {
        __builtin_trap();
    }
    exception_frames[token - 1].environment = environment;
}

NETWASM_EXPORT("exception_frame_leave")
void exception_frame_leave(u32 token)
{
    ExceptionFrame *frame;
    if (token == 0 || token != exception_frame_top) {
        __builtin_trap();
    }
    frame = &exception_frames[--exception_frame_top];
    frame->metadata_address = 0;
    frame->clause_count = 0;
    frame->environment = 0;
    frame->has_filters = 0;
}

/* A handler's landing pad: the exception has left every frame entered since
   this region began, so the arenas return to where they stood then. */
NETWASM_EXPORT("exception_frame_unwind")
void exception_frame_unwind(u32 token)
{
    if (token == 0 || token != exception_frame_top) {
        __builtin_trap();
    }
    frame_marks_restore(&exception_frames[token - 1].marks);
    exception_frame_leave(token);
}

NETWASM_EXPORT("exception_frame_target_clause")
u32 exception_frame_target_clause(u32 token)
{
    if (token == 0 || token != exception_frame_top + 1) {
        __builtin_trap();
    }
    return exception_frames[token - 1].target_clause;
}

static void dispatch_exception(netwasm_reference_t exception)
{
    dispatch_root = exception;
    for (u32 token = exception_frame_top; token > filter_search_floor; token--) {
        ExceptionFrame *frame = &exception_frames[token - 1];
        frame->target_clause = 0;
        if (!frame->has_filters) {
            const u32 *catch_types =
                (const u32 *)(uintptr_t)frame->metadata_address;
            for (u32 clause = 0; clause < frame->clause_count; clause++) {
                if (is_assignable(exception, catch_types[clause])) {
                    frame->target_clause = clause + 1;
                    return;
                }
            }
            continue;
        }
        const ExceptionClause *clauses =
            (const ExceptionClause *)(uintptr_t)frame->metadata_address;
        for (u32 clause = 0; clause < frame->clause_count; clause++) {
            if (clauses[clause].kind == 0) {
                if (!is_assignable(exception, clauses[clause].value)) {
                    continue;
                }
            } else {
                netwasm_reference_t saved_root = dispatch_root;
                u32 saved_floor = filter_search_floor;
                FrameMarks marks = frame_marks_capture();
                filter_search_floor = token;
                u32 accepted = netwasm_evaluate_filter(
                    clauses[clause].value,
                    exception,
                    frame->environment);
                frame_marks_restore(&marks);
                filter_search_floor = saved_floor;
                dispatch_root = saved_root;
                frame = &exception_frames[token - 1];
                if (!accepted) {
                    continue;
                }
            }
            {
                frame->target_clause = clause + 1;
                return;
            }
        }
    }
}

NETWASM_EXPORT("begin_throw")
void begin_throw(netwasm_reference_t exception)
{
    capture_stack_trace(exception);
    dispatch_exception(exception);
}

NETWASM_EXPORT("begin_rethrow")
void begin_rethrow(netwasm_reference_t exception)
{
    capture_stack_trace_if_missing(exception);
    dispatch_exception(exception);
}

NETWASM_EXPORT("end_catch")
void end_catch(void)
{
    dispatch_root = 0;
}

NETWASM_EXPORT("exception_get_active")
netwasm_reference_t exception_get_active(void)
{
    return dispatch_root;
}

NETWASM_EXPORT("exception_get_active_type_id")
u32 exception_get_active_type_id(void)
{
    if (dispatch_root == 0) {
        return 0;
    }
    return *(const u32 *)(uintptr_t)dispatch_root;
}

NETWASM_EXPORT("exception_clear_active")
void exception_clear_active(void)
{
    end_catch();
#ifndef NETWASM_NO_FINALIZATION
    escaped_finalizer_status = 0;
#endif
}

NETWASM_FINALIZATION_EXPORT("finalizer_safepoint")
netwasm_reference_t finalizer_safepoint(void)
{
#ifndef NETWASM_NO_FINALIZATION
    if (escaped_finalizer_status != 0) {
        return dispatch_root;
    }
    collect_internal();
    if (!draining_finalizers && collector_has_pending_finalizers()) {
        draining_finalizers = 1;
        (void)collector_invoke_finalizers();
        draining_finalizers = 0;
    }
    return escaped_finalizer_status == 0 ? 0 : dispatch_root;
#else
    return 0;
#endif
}

/* The byte offsets of a reference bitmap's set bits, one bit per reference slot. */
static const size_t *reference_offsets(
    netwasm_address_t bitmap_address,
    u32 bitmap_bits,
    size_t *count)
{
    const NetWasmReferenceBitmapWord *bitmap =
        (const NetWasmReferenceBitmapWord *)(uintptr_t)bitmap_address;
    size_t word_bits = sizeof(*bitmap) * 8;
    size_t total = 0;
    for (u32 bit = 0; bitmap != NULL && bit < bitmap_bits; bit++) {
        total += (bitmap[bit / word_bits] >> (bit % word_bits)) & 1;
    }
    *count = total;
    if (total == 0) {
        return NULL;
    }
    size_t *offsets = collector_allocate_metadata(total * sizeof(size_t));
    if (offsets == NULL) {
        __builtin_trap();
    }
    size_t index = 0;
    for (u32 bit = 0; bit < bitmap_bits; bit++) {
        if ((bitmap[bit / word_bits] >> (bit % word_bits)) & 1) {
            offsets[index++] = (size_t)bit * sizeof(netwasm_reference_t);
        }
    }
    return offsets;
}

static const size_t reference_element_offsets[] = {0};

void collector_object_layout(const void *object, NetWasmCollectorLayout *layout)
{
    netwasm_reference_t reference = (netwasm_reference_t)(uintptr_t)object;
    u32 type_id = *(const u32 *)object;
    const TypeDescriptor *type = &type_descriptors[type_id];
    *layout = (NetWasmCollectorLayout){0};
    switch (type_data_kinds[type_id]) {
    case NETWASM_OBJECT_DATA_STRING:
        layout->fixed_bytes = NETWASM_STRING_DATA_OFFSET;
        layout->element_size = sizeof(uint16_t);
        layout->element_count =
            *(const u32 *)(uintptr_t)(reference + NETWASM_STRING_LENGTH_OFFSET);
        return;
    case NETWASM_OBJECT_DATA_ARRAY: {
        const TypeDescriptor *element = &type_descriptors[
            *(const u32 *)(uintptr_t)(reference + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET)];
        /* Elements are inline, so the data pointer marks where the fields end. */
        layout->fixed_bytes = (size_t)(*(const netwasm_reference_t *)(uintptr_t)(
            reference + NETWASM_ARRAY_DATA_POINTER_OFFSET) - reference);
        layout->element_count =
            *(const u32 *)(uintptr_t)(reference + NETWASM_ARRAY_LENGTH_OFFSET);
        if (element->value_registered) {
            layout->element_offsets = element->value_reference_offsets;
            layout->element_reference_count = element->value_reference_count;
            layout->element_size = element->value_size;
        } else {
            layout->element_offsets = reference_element_offsets;
            layout->element_reference_count = 1;
            layout->element_size = sizeof(netwasm_reference_t);
        }
        return;
    }
    default:
        layout->fixed_offsets = type->reference_offsets;
        layout->fixed_reference_count = type->reference_count;
        layout->fixed_bytes = type->size;
        return;
    }
}

/* An object of a registered type; collectors read its layout from the type id. */
static void *allocate_typed(size_t bytes, const TypeDescriptor *type)
{
    return collector_allocate_object(bytes, type->reference_count != 0);
}

static void *allocate_untraced(size_t bytes)
{
    return collector_allocate_object(bytes, false);
}

NETWASM_EXPORT("register_type")
void register_type(
    u32 type_id,
    u32 base_type_id,
    netwasm_address_t size,
    netwasm_address_t bitmap_address,
    u32 bitmap_bits,
    netwasm_address_t assignable_type_ids_address,
    u32 assignable_type_id_count,
    u32 has_finalizer,
    u32 is_interface)
{
    TypeDescriptor *entry;
    if (type_id >= type_capacity) {
        __builtin_trap();
    }
    entry = &type_descriptors[type_id];
    if (entry->registered) {
        return;
    }
    entry->reference_offsets = reference_offsets(
        bitmap_address, bitmap_bits, &entry->reference_count);
    entry->size = size;
    entry->base_type_id = base_type_id;
    entry->bitmap_address = bitmap_address;
    entry->bitmap_bits = bitmap_bits;
    entry->assignable_type_ids_address = assignable_type_ids_address;
    entry->assignable_type_id_count = assignable_type_id_count;
    entry->has_finalizer = has_finalizer;
    entry->is_interface = is_interface;
    entry->registered = 1;
}

NETWASM_EXPORT("register_value_type")
void register_value_type(
    u32 type_id,
    netwasm_address_t size,
    netwasm_address_t boxed_payload_offset,
    netwasm_address_t bitmap_address,
    u32 bitmap_bits)
{
    TypeDescriptor *entry;
    if (type_id >= type_capacity || !type_descriptors[type_id].registered) {
        __builtin_trap();
    }
    entry = &type_descriptors[type_id];
    if (entry->value_registered) {
        if (entry->value_size != size) {
            __builtin_trap();
        }
        return;
    }
    entry->value_reference_offsets = reference_offsets(
        bitmap_address, bitmap_bits, &entry->value_reference_count);
    entry->value_size = size;
    entry->boxed_payload_offset = boxed_payload_offset;
    entry->value_contains_references = 0;
    for (u32 bit = 0; bitmap_address != 0 && bit < bitmap_bits; bit++) {
        const NetWasmReferenceBitmapWord *bitmap = (const NetWasmReferenceBitmapWord *)(uintptr_t)bitmap_address;
        if ((bitmap[bit / (sizeof(NetWasmReferenceBitmapWord) * 8)] &
             ((NetWasmReferenceBitmapWord)1 << (bit % (sizeof(NetWasmReferenceBitmapWord) * 8)))) != 0) {
            entry->value_contains_references = 1;
        }
    }
    entry->value_registered = 1;
}

NETWASM_EXPORT("register_static_root")
void register_static_root(netwasm_address_t address)
{
    for (u32 index = 0; index < static_root_count; index++) {
        if (static_roots[index] == address) {
            return;
        }
    }
    if (static_root_count >= static_root_capacity) {
        __builtin_trap();
    }
    static_roots[static_root_count++] = address;
}

static NetWasmStrongHandleTable strong_handle_table;

enum { FRAME_ARENA_MINIMUM_CHUNK_SIZE = 4096 };
enum { ROOT_ARENA_INITIAL_CAPACITY = 128 * 1024 };


static FrameArenaChunk *frame_arena_create_chunk(size_t required_capacity)
{
    size_t capacity = required_capacity > FRAME_ARENA_MINIMUM_CHUNK_SIZE
        ? required_capacity
        : FRAME_ARENA_MINIMUM_CHUNK_SIZE;
    if (capacity > SIZE_MAX - sizeof(FrameArenaChunk) - 15u) {
        return NULL;
    }
    FrameArenaChunk *chunk = collector_allocate_metadata(
        sizeof(FrameArenaChunk) + 15u + capacity);
    if (chunk == NULL) {
        return NULL;
    }
    chunk->next = NULL;
    chunk->previous = NULL;
    chunk->saved_cursor = 0;
    chunk->capacity = capacity;
    return chunk;
}

static inline __attribute__((always_inline)) unsigned char *frame_arena_bytes(
    FrameArenaChunk *chunk)
{
    /* Metadata allocation need only satisfy the platform's natural alignment,
       which is eight bytes in Wasm. Value-frame storage requires sixteen. */
    return (unsigned char *)(((uintptr_t)chunk->bytes + 15u) & ~(uintptr_t)15u);
}

static void *frame_arena_allocate_slow(
    FrameArena *arena,
    size_t size)
{
    FrameArenaChunk *chunk = arena->current;
    if (chunk == NULL) {
        chunk = frame_arena_create_chunk(size);
        if (chunk == NULL) {
            return NULL;
        }
    } else {
        FrameArenaChunk *next = chunk->next;
        if (next == NULL || next->capacity < size) {
            next = frame_arena_create_chunk(size);
            if (next == NULL) {
                return NULL;
            }
            next->next = chunk->next;
            chunk->next = next;
        }
        /* Do not publish a transition until allocation has succeeded. Cached
           successors can be reparented after insertion of a larger chunk. */
        chunk->saved_cursor = arena->cursor;
        next->previous = chunk;
        chunk = next;
    }

    uintptr_t start = (uintptr_t)frame_arena_bytes(chunk);
    arena->current = chunk;
    arena->begin = start;
    arena->cursor = start + size;
    arena->limit = start + chunk->capacity;
    return (void *)start;
}

static inline __attribute__((always_inline)) void *frame_arena_allocate(
    FrameArena *arena,
    size_t size)
{
    uintptr_t cursor = arena->cursor;
    if (size <= arena->limit - cursor) {
        arena->cursor = cursor + size;
        return (void *)cursor;
    }
    return frame_arena_allocate_slow(arena, size);
}

static inline __attribute__((always_inline)) void frame_arena_rewind(
    FrameArena *arena,
    FrameArenaChunk *chunk,
    void *allocation)
{
    /* The leave operation has already proved this is an active frame. */
    if (arena->current != chunk) {
        arena->current = chunk;
        arena->begin = (uintptr_t)frame_arena_bytes(chunk);
        arena->limit = arena->begin + chunk->capacity;
    }
    arena->cursor = (uintptr_t)allocation;
}

NETWASM_EXPORT("handle_new") u32 handle_new(netwasm_reference_t target)
{
    return strong_handle_table_create(&strong_handle_table, target);
}

NETWASM_EXPORT("handle_get") netwasm_reference_t handle_get(u32 handle)
{
    return strong_handle_table_get(&strong_handle_table, handle);
}

NETWASM_EXPORT("handle_release") void handle_release(u32 handle)
{
    strong_handle_table_release(&strong_handle_table, handle);
}


NETWASM_EXPORT("root_frame_enter")
netwasm_address_t root_frame_enter(u32 slot_count)
{
    if ((size_t)slot_count > SIZE_MAX / sizeof(netwasm_reference_t)) {
        return 0;
    }
    size_t slot_bytes = (size_t)slot_count * sizeof(netwasm_reference_t);
    /* Every managed root slot is published before a collecting safepoint. */
    if (root_frame_arena.current == NULL) {
        size_t capacity = slot_bytes > ROOT_ARENA_INITIAL_CAPACITY
            ? slot_bytes : ROOT_ARENA_INITIAL_CAPACITY;
        void *initial = frame_arena_allocate_slow(&root_frame_arena, capacity);
        if (initial == NULL) return 0;
        root_frame_arena.cursor = (uintptr_t)initial + slot_bytes;
        return (netwasm_address_t)(uintptr_t)initial;
    }
    void *slots = frame_arena_allocate(&root_frame_arena, slot_bytes);
    return (netwasm_address_t)(uintptr_t)slots;
}

NETWASM_EXPORT("root_frame_leave")
void root_frame_leave(netwasm_address_t frame)
{
    netwasm_reference_t *slots = (netwasm_reference_t *)(uintptr_t)frame;
    uintptr_t address = (uintptr_t)slots;
    if (root_frame_arena.current == NULL || address < root_frame_arena.begin ||
        address > root_frame_arena.cursor || address % sizeof(netwasm_reference_t) != 0) {
        __builtin_trap();
    }
    if (address == root_frame_arena.begin &&
        root_frame_arena.current->previous != NULL) {
        FrameArenaChunk *previous = root_frame_arena.current->previous;
        root_frame_arena.current = previous;
        root_frame_arena.begin = (uintptr_t)frame_arena_bytes(previous);
        root_frame_arena.limit = root_frame_arena.begin + previous->capacity;
        root_frame_arena.cursor = previous->saved_cursor;
    } else {
        root_frame_arena.cursor = address;
    }
}

NETWASM_EXPORT("value_frame_enter")
netwasm_address_t value_frame_enter(netwasm_address_t byte_count)
{
    const netwasm_address_t alignment = 16u;
    netwasm_address_t aligned_count;
    size_t frame_size;
    ValueFrame *frame;
    if (byte_count > UINT32_MAX - (alignment - 1u) ||
        byte_count > SIZE_MAX - (alignment - 1u)) {
        return 0;
    }
    aligned_count = (byte_count + alignment - 1u) & ~(alignment - 1u);
    if ((size_t)aligned_count > SIZE_MAX - sizeof(ValueFrame)) {
        return 0;
    }
    frame_size = sizeof(ValueFrame) + (size_t)aligned_count;
    frame = (ValueFrame *)frame_arena_allocate(
        &value_frame_arena,
        frame_size);
    if (frame == NULL) {
        return 0;
    }
    frame->previous = value_frame;
    frame->chunk = value_frame_arena.current;
    value_frame = frame;
    return (netwasm_address_t)(uintptr_t)frame->bytes;
}

NETWASM_EXPORT("value_frame_leave")
void value_frame_leave(netwasm_address_t frame)
{
    ValueFrame *target = value_frame;
    unsigned char *bytes = (unsigned char *)(uintptr_t)frame;
    while (target != NULL && bytes != target->bytes) {
        target = target->previous;
    }
    if (target == NULL) {
        __builtin_trap();
    }
    value_frame = target->previous;
    frame_arena_rewind(&value_frame_arena, target->chunk, target);
}

static void root_frame_rewind(uintptr_t mark)
{
    FrameArena *arena = &root_frame_arena;
    if (arena->current == NULL) {
        if (mark != 0) {
            __builtin_trap();
        }
        return;
    }
    /* A zero mark predates the first root frame: rewind to the empty arena. */
    int popped = 0;
    while (mark == 0 || !(arena->begin < mark && mark <= arena->limit)) {
        FrameArenaChunk *previous = arena->current->previous;
        if (previous == NULL) {
            if (mark != 0 && mark != arena->begin) {
                __builtin_trap();
            }
            mark = arena->begin;
            break;
        }
        arena->current = previous;
        arena->begin = (uintptr_t)frame_arena_bytes(previous);
        arena->limit = arena->begin + previous->capacity;
        popped = 1;
    }
    if (!popped && mark > arena->cursor) {
        __builtin_trap();
    }
    arena->cursor = mark;
}

static void value_frame_rewind(ValueFrame *mark)
{
    ValueFrame *oldest = NULL;
    for (ValueFrame *frame = value_frame; frame != mark; frame = frame->previous) {
        if (frame == NULL) {
            __builtin_trap();
        }
        oldest = frame;
    }
    if (oldest != NULL) {
        value_frame = mark;
        frame_arena_rewind(&value_frame_arena, oldest->chunk, oldest);
    }
}

static FrameMarks frame_marks_capture(void)
{
    return (FrameMarks){
        root_frame_arena.current == NULL ? 0 : root_frame_arena.cursor,
        value_frame,
        stack_trace_frame_top,
    };
}

static void frame_marks_restore(const FrameMarks *marks)
{
    if (marks->stack_trace > stack_trace_frame_top) {
        __builtin_trap();
    }
    root_frame_rewind(marks->root);
    value_frame_rewind(marks->value);
    stack_trace_frame_top = marks->stack_trace;
}

static netwasm_reference_t allocate_registered(
    netwasm_address_t size,
    u32 type_id)
{
    void *allocation;
    collect_for_allocation_test();
#ifndef NETWASM_RUNTIME_PACK
    if (fail_next_allocation) {
        fail_next_allocation = 0;
        return 0;
    }
#endif
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        size != type_descriptors[type_id].size) {
        __builtin_trap();
    }
    allocation = allocate_typed(size, &type_descriptors[type_id]);
    if (allocation == NULL) {
        return 0;
    }
    ((u32 *)allocation)[0] = type_id;
    allocation_root = (uintptr_t)allocation;
#ifndef NETWASM_RUNTIME_PACK
    allocation_count++;
#endif
    track(allocation);
    allocation_root = 0;
    type_data_kinds[type_id] = NETWASM_OBJECT_DATA_FIELDS;
    return (netwasm_reference_t)(uintptr_t)allocation;
}

NETWASM_EXPORT("allocate")
netwasm_reference_t allocate(netwasm_address_t size, u32 type_id)
{
    if (type_id >= type_capacity || type_descriptors[type_id].has_finalizer) {
        __builtin_trap();
    }
    return allocate_registered(size, type_id);
}

NETWASM_EXPORT("boxed_value_clone")
netwasm_reference_t boxed_value_clone(netwasm_reference_t source)
{
    u32 type_id;
    TypeDescriptor *entry;
    netwasm_reference_t clone;
    if (source == 0) {
        __builtin_trap();
    }
    type_id = *(const u32 *)(uintptr_t)source;
    if (type_id >= type_capacity) {
        __builtin_trap();
    }
    entry = &type_descriptors[type_id];
    if (!entry->registered || !entry->value_registered ||
        entry->boxed_payload_offset > entry->size ||
        entry->value_size > entry->size - entry->boxed_payload_offset) {
        __builtin_trap();
    }
    clone = allocate_registered(entry->size, type_id);
    if (clone == 0) {
        return 0;
    }
    collector_move_value_range(
        clone,
        (void *)(uintptr_t)(clone + entry->boxed_payload_offset),
        (const void *)(uintptr_t)(source + entry->boxed_payload_offset),
        1,
        entry->value_size);
    return clone;
}

NETWASM_EXPORT("runtime_helpers_equals")
u32 runtime_helpers_equals(
    netwasm_reference_t left,
    netwasm_reference_t right)
{
    u32 left_type_id;
    u32 right_type_id;
    netwasm_address_t index;
    TypeDescriptor *entry;
    const unsigned char *left_bytes;
    const unsigned char *right_bytes;
    if (left == right) {
        return 1;
    }
    if (left == 0 || right == 0) {
        return 0;
    }
    left_type_id = *(const u32 *)(uintptr_t)left;
    right_type_id = *(const u32 *)(uintptr_t)right;
    if (left_type_id >= type_capacity || right_type_id >= type_capacity) {
        __builtin_trap();
    }
    if (left_type_id != right_type_id) {
        return 0;
    }
    entry = &type_descriptors[left_type_id];
    if (!entry->registered || !entry->value_registered) {
        return 0;
    }
    if (entry->boxed_payload_offset > entry->size ||
        entry->value_size > entry->size - entry->boxed_payload_offset) {
        __builtin_trap();
    }
    left_bytes = (const unsigned char *)(uintptr_t)(
        left + entry->boxed_payload_offset);
    right_bytes = (const unsigned char *)(uintptr_t)(
        right + entry->boxed_payload_offset);
    for (index = 0; index < entry->value_size; index++) {
        if (left_bytes[index] != right_bytes[index]) {
            return 0;
        }
    }
    return 1;
}

NETWASM_FINALIZATION_EXPORT("allocate_finalizable")
netwasm_reference_t allocate_finalizable(netwasm_address_t size, u32 type_id)
{
    netwasm_reference_t object;
#ifndef NETWASM_NO_FINALIZATION
    void *allocation;
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        size != type_descriptors[type_id].size ||
        !type_descriptors[type_id].has_finalizer) {
        __builtin_trap();
    }
    object = allocate_registered(size, type_id);
    allocation = (void *)(uintptr_t)object;
    if (allocation == NULL) {
        return 0;
    }
    allocation_root = (uintptr_t)allocation;
    collector_register_finalizer(
        allocation,
        managed_finalizer,
        (void *)(uintptr_t)type_id,
        NULL,
        NULL);
    allocation_root = 0;
    return object;
#else
    object = allocate_registered(size, type_id);
    return object;
#endif
}

static int is_power_of_two(netwasm_address_t value)
{
    return value != 0 && (value & (value - 1)) == 0;
}

NETWASM_EXPORT("component_free")
void component_free(netwasm_address_t address)
{
    ComponentAllocationHeader *header;
    if (address == 0) {
        return;
    }
#ifndef NETWASM_RUNTIME_PACK
    if (component_active_allocation_count == 0) {
        __builtin_trap();
    }
#endif
    header = (ComponentAllocationHeader *)(uintptr_t)address - 1;
#ifndef NETWASM_RUNTIME_PACK
    component_active_allocation_count--;
#endif
        netwasm_unmanaged_free(header->allocation);
}

NETWASM_EXPORT("component_realloc")
netwasm_address_t component_realloc(
    netwasm_address_t old_address,
    netwasm_address_t old_size,
    netwasm_address_t alignment,
    netwasm_address_t new_size)
{
    netwasm_address_t allocation_size;
    netwasm_address_t allocation_address;
    netwasm_address_t aligned_address;
    ComponentAllocationHeader *header;
    void *allocation;
    size_t copy_size;

    if (new_size == 0) {
        if (old_size != 0) {
            component_free(old_address);
        }
        return 0;
    }
    if (!is_power_of_two(alignment)) {
        __builtin_trap();
    }
#ifndef NETWASM_RUNTIME_PACK
    if (fail_next_component_allocation) {
        fail_next_component_allocation = 0;
        return 0;
    }
#endif
    if (alignment < sizeof(void *)) {
        alignment = sizeof(void *);
    }
    if (new_size > UINTPTR_MAX - sizeof(ComponentAllocationHeader) -
            (alignment - 1)) {
        return 0;
    }
#ifdef NETWASM_FORCE_COMPONENT_COLLECTION
    if (initialized) {
        collect_internal();
    }
#endif
    allocation_size = new_size + sizeof(ComponentAllocationHeader) +
        alignment - 1;
        allocation = netwasm_unmanaged_allocate((size_t)allocation_size);
    if (allocation == NULL) {
        return 0;
    }
    allocation_address = (netwasm_address_t)(uintptr_t)allocation +
        sizeof(ComponentAllocationHeader);
    aligned_address = netwasm_align_address(allocation_address, alignment);
    header = (ComponentAllocationHeader *)(uintptr_t)aligned_address - 1;
    header->allocation = allocation;
    header->size = new_size;
    header->alignment = alignment;
#ifndef NETWASM_RUNTIME_PACK
    component_active_allocation_count++;
#endif
    if (old_size != 0) {
        copy_size = old_size < new_size ? (size_t)old_size : (size_t)new_size;
        memcpy((void *)(uintptr_t)aligned_address,
               (const void *)(uintptr_t)old_address,
               copy_size);
        component_free(old_address);
    }
    return aligned_address;
}

NETWASM_EXPORT("native_alloc")
netwasm_address_t native_alloc(netwasm_address_t size)
{
    return component_realloc(0, 0, sizeof(void *), size == 0 ? 1 : size);
}

NETWASM_EXPORT("native_realloc")
netwasm_address_t native_realloc(
    netwasm_address_t address,
    netwasm_address_t size)
{
    ComponentAllocationHeader *header;
    netwasm_address_t old_size = 0;
    if (address != 0) {
        header = (ComponentAllocationHeader *)(uintptr_t)address - 1;
        old_size = header->size;
    }
    return component_realloc(
        address,
        old_size,
        sizeof(void *),
        size == 0 ? 1 : size);
}

NETWASM_EXPORT("native_free")
void native_free(netwasm_address_t address)
{
    component_free(address);
}

NETWASM_EXPORT("native_aligned_alloc")
netwasm_address_t native_aligned_alloc(
    netwasm_address_t size,
    netwasm_address_t alignment)
{
    return component_realloc(0, 0, alignment, size == 0 ? 1 : size);
}

NETWASM_EXPORT("native_aligned_realloc")
netwasm_address_t native_aligned_realloc(
    netwasm_address_t address,
    netwasm_address_t size,
    netwasm_address_t alignment)
{
    ComponentAllocationHeader *header;
    netwasm_address_t old_size = 0;
    if (address != 0) {
        header = (ComponentAllocationHeader *)(uintptr_t)address - 1;
        old_size = header->size;
    }
    return component_realloc(address, old_size, alignment, size == 0 ? 1 : size);
}

NETWASM_EXPORT("native_aligned_free")
void native_aligned_free(netwasm_address_t address)
{
    component_free(address);
}

#ifndef NETWASM_RUNTIME_PACK
NETWASM_TEST_EXPORT("test_component_active_allocation_count")
u32 test_component_active_allocation_count(void)
{
    return component_active_allocation_count;
}

NETWASM_TEST_EXPORT("test_fail_next_component_allocation")
void test_fail_next_component_allocation(void)
{
    fail_next_component_allocation = 1;
}
#endif

NETWASM_EXPORT("get_type_object")
netwasm_reference_t get_type_object(u32 semantic_type_id, u32 facade_type_id)
{
    netwasm_reference_t object;
    if (semantic_type_id == 0 || semantic_type_id >= type_capacity ||
        !type_descriptors[semantic_type_id].registered ||
        facade_type_id == 0 || facade_type_id >= type_capacity ||
        !type_descriptors[facade_type_id].registered ||
        type_descriptors[facade_type_id].size != netwasm_align_address(
            NETWASM_OBJECT_HEADER_SIZE + sizeof(u32),
            NETWASM_OBJECT_REFERENCE_SIZE)) {
        __builtin_trap();
    }
    object = type_object_cache[semantic_type_id];
    if (object != 0) {
        return object;
    }
    object = allocate(
        netwasm_align_address(
            NETWASM_OBJECT_HEADER_SIZE + sizeof(u32),
            NETWASM_OBJECT_REFERENCE_SIZE),
        facade_type_id);
    if (object == 0) {
        return 0;
    }
    allocation_root = object;
    *(u32 *)(uintptr_t)(object + NETWASM_OBJECT_HEADER_SIZE) = semantic_type_id;
    type_object_cache[semantic_type_id] = object;
    if (collector_register_short_weak_reference(
            (uintptr_t *)&type_object_cache[semantic_type_id],
            (uintptr_t)object) == 0) {
        type_object_cache[semantic_type_id] = 0;
        allocation_root = 0;
        __builtin_trap();
    }
    allocation_root = 0;
    return object;
}

/* An array is one allocation: its fields, then its elements from
   NETWASM_ARRAY_INLINE_DATA_OFFSET. An empty array keeps one spare word so its
   data pointer stays inside the allocation for any collector. */
static netwasm_reference_t allocate_inline_array(
    u32 type_id,
    u32 element_type_id,
    u32 length,
    size_t element_size,
    bool contains_references)
{
    size_t bytes;
    netwasm_reference_t array;
    if (element_size != 0 &&
        (size_t)length > (SIZE_MAX - NETWASM_ARRAY_INLINE_DATA_OFFSET -
            NETWASM_OBJECT_REFERENCE_SIZE) / element_size) {
        return 0;
    }
    bytes = NETWASM_ARRAY_INLINE_DATA_OFFSET + (size_t)length * element_size;
    if (length == 0) {
        bytes += NETWASM_OBJECT_REFERENCE_SIZE;
    }
    type_data_kinds[type_id] = NETWASM_OBJECT_DATA_ARRAY;
    array = (netwasm_reference_t)(uintptr_t)collector_allocate_object(
        bytes, contains_references && length != 0);
    if (array == 0) {
        return 0;
    }
    *(u32 *)(uintptr_t)array = type_id;
    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_LENGTH_OFFSET) = length;
    *(netwasm_reference_t *)(uintptr_t)(array + NETWASM_ARRAY_DATA_POINTER_OFFSET) =
        array + NETWASM_ARRAY_INLINE_DATA_OFFSET;
    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET) =
        element_type_id;
#ifndef NETWASM_RUNTIME_PACK
    allocation_count++;
#endif
    allocation_root = array;
    track((void *)(uintptr_t)array);
    allocation_root = 0;
    return array;
}

NETWASM_EXPORT("allocate_reference_array")
netwasm_reference_t allocate_reference_array(
    u32 length,
    u32 type_id,
    u32 element_type_id)
{
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        type_descriptors[type_id].size != NETWASM_ARRAY_OBJECT_SIZE ||
        element_type_id >= type_capacity || !type_descriptors[element_type_id].registered) {
        __builtin_trap();
    }
    if ((size_t)length > SIZE_MAX / sizeof(netwasm_reference_t)) {
        return 0;
    }
    collect_for_allocation_test();
    return allocate_inline_array(
        type_id, element_type_id, length, sizeof(netwasm_reference_t), true);
}

NETWASM_EXPORT("allocate_value_array")
netwasm_reference_t allocate_value_array(
    u32 length,
    u32 type_id,
    u32 element_type_id,
    u32 element_size)
{
    TypeDescriptor *element;
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        type_descriptors[type_id].size != NETWASM_ARRAY_OBJECT_SIZE ||
        element_type_id >= type_capacity) {
        __builtin_trap();
    }
    element = &type_descriptors[element_type_id];
    if (!element->registered || !element->value_registered ||
        element->value_size != element_size ||
        (element->value_size != 0 && (size_t)length > SIZE_MAX / element->value_size)) {
        __builtin_trap();
    }
    collect_for_allocation_test();
    return allocate_inline_array(type_id, element_type_id, length,
        element->value_size, element->value_contains_references != 0);
}

NETWASM_EXPORT("allocate_rectangular_array")
netwasm_reference_t allocate_rectangular_array(
    u32 rank,
    netwasm_address_t dimensions_address,
    u32 type_id,
    u32 element_type_id,
    u32 element_size,
    u32 elements_are_references)
{
    TypeDescriptor *element;
    u32 *dimensions = (u32 *)(uintptr_t)dimensions_address;
    u32 *shape;
    u32 total = 1;
    u32 stride = 1;
    netwasm_reference_t array;
    if (rank == 0 || rank > NETWASM_MAX_ARRAY_RANK || dimensions == NULL ||
        type_id >= type_capacity || !type_descriptors[type_id].registered ||
        type_descriptors[type_id].size != NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE ||
        element_type_id >= type_capacity) {
        __builtin_trap();
    }
    element = &type_descriptors[element_type_id];
    if (!element->registered ||
        (elements_are_references == 0 &&
         (!element->value_registered || element->value_size != element_size)) ||
        (elements_are_references != 0 && element_size != 0)) {
        __builtin_trap();
    }
    for (u32 dimension = rank; dimension != 0; dimension--) {
        u32 length = dimensions[dimension - 1];
        if ((int32_t)length < 0) {
            __builtin_trap();
        }
        if (length > 1073741823u ||
            (length != 0 && total > 1073741823u / length)) {
            return 0;
        }
        total *= length;
    }
    if ((size_t)rank > SIZE_MAX / NETWASM_RECTANGULAR_ARRAY_DIMENSION_SIZE ||
        (elements_are_references != 0 &&
         (size_t)total > SIZE_MAX / sizeof(netwasm_reference_t)) ||
        (elements_are_references == 0 && element_size != 0 &&
         (size_t)total > SIZE_MAX / element_size)) {
        return 0;
    }

    collect_for_allocation_test();
    {
        /* One allocation: the fields, the shape with room for lower bounds,
           then the elements. */
        size_t element_bytes = elements_are_references != 0
            ? sizeof(netwasm_reference_t)
            : element->value_size;
        size_t shape_offset = NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE;
        size_t data_offset = (shape_offset + (size_t)rank * 3 * sizeof(u32) +
            NETWASM_INLINE_DATA_ALIGNMENT - 1) &
            ~(size_t)(NETWASM_INLINE_DATA_ALIGNMENT - 1);
        size_t bytes;
        if (element_bytes != 0 && (size_t)total >
                (SIZE_MAX - data_offset - NETWASM_OBJECT_REFERENCE_SIZE) / element_bytes) {
            return 0;
        }
        bytes = data_offset + (size_t)total * element_bytes;
        if (total == 0) {
            bytes += NETWASM_OBJECT_REFERENCE_SIZE;
        }
        type_data_kinds[type_id] = NETWASM_OBJECT_DATA_ARRAY;
        array = (netwasm_reference_t)(uintptr_t)collector_allocate_object(bytes,
            total != 0 &&
                (elements_are_references != 0 || element->value_contains_references));
        if (array == 0) {
            return 0;
        }
        *(u32 *)(uintptr_t)array = type_id;
        shape = (u32 *)(uintptr_t)(array + shape_offset);
        *(netwasm_reference_t *)(uintptr_t)(
            array + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET) =
            (netwasm_reference_t)(uintptr_t)shape;
        for (u32 dimension = rank; dimension != 0; dimension--) {
            u32 index = dimension - 1;
            shape[index * 2] = dimensions[index];
            shape[index * 2 + 1] = stride;
            stride *= dimensions[index];
        }
        *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_LENGTH_OFFSET) = total;
        *(netwasm_reference_t *)(uintptr_t)(
            array + NETWASM_ARRAY_DATA_POINTER_OFFSET) = array + data_offset;
        *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET) =
            element_type_id;
        *(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET) = rank;
#ifndef NETWASM_RUNTIME_PACK
        allocation_count++;
#endif
        allocation_root = array;
        track((void *)(uintptr_t)array);
        allocation_root = 0;
        return array;
    }
}

NETWASM_EXPORT("array_rank")
u32 array_rank(netwasm_reference_t array)
{
    u32 type_id;
    if (array == 0) {
        __builtin_trap();
    }
    type_id = *(u32 *)(uintptr_t)array;
    if (type_id >= type_capacity || !type_descriptors[type_id].registered) {
        __builtin_trap();
    }
    return type_descriptors[type_id].size == NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE
        ? *(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET) & 0x7fffffffu
        : 1;
}

NETWASM_EXPORT("array_get_length")
int32_t array_get_length(netwasm_reference_t array, int32_t dimension)
{
    u32 type_id;
    u32 rank;
    u32 *shape;
    if (array == 0) {
        __builtin_trap();
    }
    type_id = *(u32 *)(uintptr_t)array;
    if (type_id >= type_capacity || !type_descriptors[type_id].registered) {
        __builtin_trap();
    }
    if (type_descriptors[type_id].size != NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE) {
        return dimension == 0
            ? (int32_t)*(u32 *)(uintptr_t)(array + NETWASM_ARRAY_LENGTH_OFFSET)
            : -1;
    }
    rank = *(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET) & 0x7fffffffu;
    if (dimension < 0 || (u32)dimension >= rank) {
        return -1;
    }
    shape = (u32 *)(uintptr_t)*(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET);
    return (int32_t)shape[(u32)dimension * 2];
}

/* Bounds extend only the shape owned by an explicitly bounded ARRAY. The
   existing vector/object headers and zero-bound shape records are unchanged. */
NETWASM_EXPORT("allocate_bounded_rectangular_array")
netwasm_reference_t allocate_bounded_rectangular_array(
    u32 rank,
    netwasm_address_t dimensions_address,
    u32 type_id,
    u32 element_type_id,
    u32 element_size,
    u32 elements_are_references)
{
    netwasm_reference_t array = allocate_rectangular_array(
        rank, dimensions_address, type_id, element_type_id,
        element_size, elements_are_references);
    if (array == 0) {
        return 0;
    }
    {
        /* The inline shape already has room for the lower bounds. */
        u32 *shape = (u32 *)(uintptr_t)*(netwasm_reference_t *)(uintptr_t)(
            array + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET);
        memcpy(shape + rank * 2, (u32 *)(uintptr_t)dimensions_address + rank,
            (size_t)rank * sizeof(u32));
        *(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET) =
            rank | 0x80000000u;
        return array;
    }
}

NETWASM_EXPORT("array_get_lower_bound")
int32_t array_get_lower_bound(netwasm_reference_t array, u32 dimension)
{
    u32 rank = array_rank(array);
    if (dimension >= rank) {
        __builtin_trap();
    }
    u32 type_id = *(u32 *)(uintptr_t)array;
    if (type_descriptors[type_id].size != NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE ||
        (*(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET) &
            0x80000000u) == 0) {
        return 0;
    }
    u32 *shape = (u32 *)(uintptr_t)*(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET);
    return (int32_t)shape[rank * 2 + dimension];
}

/* Returns address 1 when boxing cannot allocate. Zero remains a valid null
   reference array element, so the managed emitter translates only the
   reserved sentinel into OutOfMemoryException. */
NETWASM_EXPORT("array_get_value")
netwasm_reference_t array_get_value(netwasm_reference_t array, u32 index,
    u32 nullable_underlying_type_id, netwasm_address_t nullable_value_offset)
{
    u32 array_type_id;
    u32 element_type_id;
    u32 length;
    TypeDescriptor *element;
    netwasm_reference_t data;
    netwasm_reference_t boxed;
    netwasm_address_t slot_offset;

    if (array == 0) {
        __builtin_trap();
    }
    array_type_id = *(u32 *)(uintptr_t)array;
    if (array_type_id >= type_capacity ||
        type_data_kinds[array_type_id] != NETWASM_OBJECT_DATA_ARRAY) {
        __builtin_trap();
    }
    length = *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_LENGTH_OFFSET);
    if (index >= length) {
        __builtin_trap();
    }
    element_type_id = *(u32 *)(uintptr_t)(
        array + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET);
    if (element_type_id >= type_capacity ||
        !type_descriptors[element_type_id].registered) {
        __builtin_trap();
    }
    element = &type_descriptors[element_type_id];
    data = *(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    if (!element->value_registered) {
        return ((netwasm_reference_t *)(uintptr_t)data)[index];
    }

    slot_offset = (netwasm_address_t)index * element->value_size;
    if (nullable_underlying_type_id != 0) {
        if (*(const unsigned char *)(uintptr_t)(data + slot_offset) == 0) {
            return 0;
        }
        if (nullable_underlying_type_id >= type_capacity ||
            !type_descriptors[nullable_underlying_type_id].value_registered) {
            __builtin_trap();
        }
        slot_offset += nullable_value_offset;
        element_type_id = nullable_underlying_type_id;
        element = &type_descriptors[element_type_id];
    }
    allocation_root = array;
    collect_for_allocation_test();
    boxed = (netwasm_reference_t)(uintptr_t)allocate_typed(element->size, element);
    if (boxed == 0) {
        allocation_root = 0;
        return (netwasm_reference_t)1;
    }
    *(u32 *)(uintptr_t)boxed = element_type_id;
    allocation_root = boxed;
    collector_move_value_range(
        boxed,
        (void *)(uintptr_t)(boxed + element->boxed_payload_offset),
        (const void *)(uintptr_t)(data + slot_offset),
        1,
        element->value_size);
#ifndef NETWASM_RUNTIME_PACK
    allocation_count++;
#endif
    track((void *)(uintptr_t)boxed);
    type_data_kinds[element_type_id] = NETWASM_OBJECT_DATA_FIELDS;
    allocation_root = 0;
    return boxed;
}

NETWASM_EXPORT("array_copy")
u32 array_copy(
    netwasm_reference_t source,
    u32 source_index,
    netwasm_reference_t destination,
    u32 destination_index,
    u32 length)
{
    u32 source_type_id;
    u32 destination_type_id;
    u32 source_element_type_id;
    u32 destination_element_type_id;
    TypeDescriptor *source_element;
    TypeDescriptor *destination_element;
    netwasm_reference_t source_data;
    netwasm_reference_t destination_data;

    enum {
        ARRAY_COPY_SUCCESS = 0,
        ARRAY_COPY_TYPE_MISMATCH = 1,
        ARRAY_COPY_ELEMENT_CAST_FAILURE = 2,
        ARRAY_COPY_OUT_OF_MEMORY = 3
    };

    if (source == 0 || destination == 0) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    source_type_id = *(u32 *)(uintptr_t)source;
    destination_type_id = *(u32 *)(uintptr_t)destination;
    if (source_type_id >= type_capacity || destination_type_id >= type_capacity ||
        type_data_kinds[source_type_id] != NETWASM_OBJECT_DATA_ARRAY ||
        type_data_kinds[destination_type_id] != NETWASM_OBJECT_DATA_ARRAY) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    source_element_type_id = *(u32 *)(uintptr_t)(
        source + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET);
    destination_element_type_id = *(u32 *)(uintptr_t)(
        destination + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET);
    if (source_element_type_id >= type_capacity ||
        destination_element_type_id >= type_capacity) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    source_element = &type_descriptors[source_element_type_id];
    destination_element = &type_descriptors[destination_element_type_id];
    if (!source_element->registered || !destination_element->registered) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    if (source_element->value_registered == destination_element->value_registered) {
        if (!is_type_assignable(source_type_id, destination_type_id) &&
            !is_type_assignable(destination_type_id, source_type_id) &&
            !source_element->is_interface && !destination_element->is_interface) {
            return ARRAY_COPY_TYPE_MISMATCH;
        }
    } else if (source_element->value_registered) {
        if (!is_type_assignable(source_element_type_id, destination_element_type_id)) {
            return ARRAY_COPY_TYPE_MISMATCH;
        }
    } else if (!is_type_assignable(
            destination_element_type_id,
            source_element_type_id)) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    if (source_element->value_registered &&
        destination_element->value_registered &&
        source_element->value_size != destination_element->value_size) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    if (length == 0) {
        return ARRAY_COPY_SUCCESS;
    }
    source_data = *(netwasm_reference_t *)(uintptr_t)(
        source + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    destination_data = *(netwasm_reference_t *)(uintptr_t)(
        destination + NETWASM_ARRAY_DATA_POINTER_OFFSET);

    if (source_element->value_registered &&
        !destination_element->value_registered) {
        netwasm_reference_t *destination_values =
            (netwasm_reference_t *)(uintptr_t)destination_data + destination_index;
        for (u32 offset = 0; offset < length; offset++) {
            netwasm_reference_t boxed;
            collect_for_allocation_test();
            boxed = (netwasm_reference_t)(uintptr_t)allocate_typed(
                source_element->size, source_element);
            if (boxed == 0) {
                return ARRAY_COPY_OUT_OF_MEMORY;
            }
            *(u32 *)(uintptr_t)boxed = source_element_type_id;
            allocation_root = boxed;
            collector_move_value_range(
                boxed,
                (void *)(uintptr_t)(boxed + source_element->boxed_payload_offset),
                (const void *)(uintptr_t)(source_data +
                    (netwasm_address_t)(source_index + offset) *
                        source_element->value_size),
                1,
                source_element->value_size);
#ifndef NETWASM_RUNTIME_PACK
            allocation_count++;
#endif
            track((void *)(uintptr_t)boxed);
            type_data_kinds[source_element_type_id] = NETWASM_OBJECT_DATA_FIELDS;
            collector_store_reference(
                destination,
                &destination_values[offset],
                boxed);
            allocation_root = 0;
        }
        return ARRAY_COPY_SUCCESS;
    }

    if (!source_element->value_registered &&
        destination_element->value_registered) {
        netwasm_reference_t *source_values =
            (netwasm_reference_t *)(uintptr_t)source_data + source_index;
        for (u32 offset = 0; offset < length; offset++) {
            netwasm_reference_t boxed = source_values[offset];
            u32 boxed_type_id;
            if (boxed == 0) {
                return ARRAY_COPY_ELEMENT_CAST_FAILURE;
            }
            boxed_type_id = *(u32 *)(uintptr_t)boxed;
            if (boxed_type_id != destination_element_type_id) {
                return ARRAY_COPY_ELEMENT_CAST_FAILURE;
            }
            collector_move_value_range(
                destination,
                (void *)(uintptr_t)(destination_data +
                    (netwasm_address_t)(destination_index + offset) *
                        destination_element->value_size),
                (const void *)(uintptr_t)(boxed +
                    destination_element->boxed_payload_offset),
                1,
                destination_element->value_size);
        }
        return ARRAY_COPY_SUCCESS;
    }

    if (!source_element->value_registered && !destination_element->value_registered) {
        netwasm_reference_t *source_values =
            (netwasm_reference_t *)(uintptr_t)source_data + source_index;
        netwasm_reference_t *destination_values =
            (netwasm_reference_t *)(uintptr_t)destination_data + destination_index;
        if (source == destination && destination_index > source_index &&
            destination_index < source_index + length) {
            for (u32 offset = length; offset != 0; offset--) {
                netwasm_reference_t value = source_values[offset - 1];
                if (value != 0 &&
                    !is_assignable(value, destination_element_type_id)) {
                    return ARRAY_COPY_ELEMENT_CAST_FAILURE;
                }
                collector_store_reference(
                    destination,
                    &destination_values[offset - 1],
                    value);
            }
        } else {
            for (u32 offset = 0; offset < length; offset++) {
                netwasm_reference_t value = source_values[offset];
                if (value != 0 &&
                    !is_assignable(value, destination_element_type_id)) {
                    return ARRAY_COPY_ELEMENT_CAST_FAILURE;
                }
                collector_store_reference(
                    destination,
                    &destination_values[offset],
                    value);
            }
        }
        return ARRAY_COPY_SUCCESS;
    }

    collector_move_value_range(
        destination,
        (void *)(uintptr_t)(destination_data +
            (netwasm_address_t)destination_index * destination_element->value_size),
        (const void *)(uintptr_t)(source_data +
            (netwasm_address_t)source_index * source_element->value_size),
        length,
        source_element->value_size);
    return ARRAY_COPY_SUCCESS;
}

NETWASM_EXPORT("array_clone")
netwasm_reference_t array_clone(
    netwasm_reference_t source,
    u32 source_index,
    u32 requested_length)
{
    u32 type_id;
    u32 element_type_id;
    u32 source_length;
    u32 length;
    TypeDescriptor *element;
    netwasm_reference_t clone;
    netwasm_reference_t source_data;
    netwasm_reference_t clone_data;

    if (source == 0) {
        __builtin_trap();
    }
    type_id = *(u32 *)(uintptr_t)source;
    if (type_id >= type_capacity ||
        type_data_kinds[type_id] != NETWASM_OBJECT_DATA_ARRAY) {
        __builtin_trap();
    }
    element_type_id = *(u32 *)(uintptr_t)(
        source + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET);
    if (element_type_id >= type_capacity ||
        !type_descriptors[element_type_id].registered) {
        __builtin_trap();
    }
    element = &type_descriptors[element_type_id];
    source_length = *(u32 *)(uintptr_t)(source + NETWASM_ARRAY_LENGTH_OFFSET);
    if (requested_length == UINT32_MAX) {
        source_index = 0;
        length = source_length;
    } else {
        length = requested_length;
        if (source_index > source_length || length > source_length - source_index) {
            __builtin_trap();
        }
    }

    if (type_descriptors[type_id].size == NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE) {
        if (requested_length != UINT32_MAX) {
            __builtin_trap();
        }
        u32 rank_flags = *(u32 *)(uintptr_t)(
            source + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET);
        u32 rank = rank_flags & 0x7fffffffu;
        u32 *shape = (u32 *)(uintptr_t)*(netwasm_reference_t *)(uintptr_t)(
            source + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET);
        u32 dimensions[NETWASM_MAX_ARRAY_RANK];
        for (u32 dimension = 0; dimension < rank; dimension++) {
            dimensions[dimension] = shape[dimension * 2];
        }
        clone = allocate_rectangular_array(
            rank,
            (netwasm_address_t)(uintptr_t)dimensions,
            type_id,
            element_type_id,
            element->value_registered ? element->value_size : 0,
            element->value_registered ? 0 : 1);
        if (clone != 0 && (rank_flags & 0x80000000u) != 0) {
            /* Each array carries its shape inline, room for lower bounds included. */
            memcpy((u32 *)(uintptr_t)*(netwasm_reference_t *)(uintptr_t)(
                    clone + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET) + rank * 2,
                shape + rank * 2, (size_t)rank * sizeof(u32));
            *(u32 *)(uintptr_t)(clone + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET) = rank_flags;
        }
    } else if (type_descriptors[type_id].size == NETWASM_ARRAY_OBJECT_SIZE) {
        clone = element->value_registered
            ? allocate_value_array(
                length,
                type_id,
                element_type_id,
                element->value_size)
            : allocate_reference_array(length, type_id, element_type_id);
    } else {
        __builtin_trap();
    }

    if (clone == 0) {
        return 0;
    }
    if (length == 0) {
        return clone;
    }
    source_data = *(netwasm_reference_t *)(uintptr_t)(
        source + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    clone_data = *(netwasm_reference_t *)(uintptr_t)(
        clone + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    if (!element->value_registered) {
        netwasm_reference_t *source_values =
            (netwasm_reference_t *)(uintptr_t)source_data;
        netwasm_reference_t *clone_values =
            (netwasm_reference_t *)(uintptr_t)clone_data;
        for (u32 index = 0; index < length; index++) {
            collector_store_reference(
                clone,
                &clone_values[index],
                source_values[source_index + index]);
        }
    } else {
        collector_move_value_range(
            clone,
            (void *)(uintptr_t)clone_data,
            (const void *)(uintptr_t)(source_data +
                (netwasm_address_t)source_index * element->value_size),
            length,
            element->value_size);
    }
    return clone;
}

NETWASM_EXPORT("array_clear")
void array_clear(netwasm_reference_t array, u32 index, u32 length)
{
    u32 type_id;
    u32 element_type_id;
    TypeDescriptor *element;
    netwasm_reference_t data;

    if (array == 0) {
        __builtin_trap();
    }
    type_id = *(u32 *)(uintptr_t)array;
    if (type_id >= type_capacity ||
        type_data_kinds[type_id] != NETWASM_OBJECT_DATA_ARRAY) {
        __builtin_trap();
    }
    element_type_id = *(u32 *)(uintptr_t)(
        array + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET);
    if (element_type_id >= type_capacity ||
        !type_descriptors[element_type_id].registered) {
        __builtin_trap();
    }
    if (length == 0) {
        return;
    }
    element = &type_descriptors[element_type_id];
    data = *(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    if (!element->value_registered) {
        netwasm_reference_t *values =
            (netwasm_reference_t *)(uintptr_t)data + index;
        for (u32 offset = 0; offset < length; offset++) {
            collector_store_reference(array, &values[offset], 0);
        }
        return;
    }
    collector_clear_value_range(
        array,
        (void *)(uintptr_t)(data +
            (netwasm_address_t)index * element->value_size),
        length,
        element->value_size);
}

NETWASM_EXPORT("allocate_string")
netwasm_reference_t allocate_string(u32 value, u32 length, u32 type_id)
{
    netwasm_address_t byte_count;
    netwasm_reference_t string;
    uint16_t *characters;
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        type_descriptors[type_id].size != netwasm_align_address(
            NETWASM_STRING_MIN_OBJECT_SIZE,
            NETWASM_OBJECT_REFERENCE_SIZE)) {
        __builtin_trap();
    }
    if ((netwasm_address_t)length >
        (UINTPTR_MAX - NETWASM_STRING_DATA_OFFSET) / sizeof(uint16_t)) {
        return 0;
    }
    collect_for_allocation_test();
#ifndef NETWASM_RUNTIME_PACK
    if (fail_next_allocation) {
        fail_next_allocation = 0;
        return 0;
    }
#endif
    byte_count = NETWASM_STRING_DATA_OFFSET + length * sizeof(uint16_t);
    type_data_kinds[type_id] = NETWASM_OBJECT_DATA_STRING;
    string = (netwasm_reference_t)(uintptr_t)allocate_untraced(byte_count);
    if (string == 0) {
        return 0;
    }
    *(u32 *)(uintptr_t)string = type_id;
    *(u32 *)(uintptr_t)(string + NETWASM_STRING_LENGTH_OFFSET) = length;
    characters = (uint16_t *)(uintptr_t)(string + NETWASM_STRING_DATA_OFFSET);
    if (value != 0) {
        for (u32 index = 0; index < length; index++) {
            characters[index] = (uint16_t)value;
        }
    }
    allocation_root = string;
#ifndef NETWASM_RUNTIME_PACK
    allocation_count++;
#endif
    track((void *)(uintptr_t)string);
    allocation_root = 0;
    return string;
}

NETWASM_FINALIZATION_EXPORT("suppress_finalize")
void suppress_finalize(netwasm_reference_t object)
{
    if (object == 0) {
        __builtin_trap();
    }
#ifndef NETWASM_NO_FINALIZATION
    allocation_root = object;
    collector_register_finalizer(
        (void *)(uintptr_t)object,
        NULL,
        NULL,
        NULL,
        NULL);
    allocation_root = 0;
#endif
}

NETWASM_FINALIZATION_EXPORT("reregister_for_finalize")
void reregister_for_finalize(netwasm_reference_t object)
{
    u32 type_id;
    if (object == 0) {
        __builtin_trap();
    }
    type_id = *(u32 *)(uintptr_t)object;
#ifndef NETWASM_NO_FINALIZATION
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        !type_descriptors[type_id].has_finalizer) {
        __builtin_trap();
    }
    allocation_root = object;
    collector_register_finalizer(
        (void *)(uintptr_t)object,
        managed_finalizer,
        (void *)(uintptr_t)type_id,
        NULL,
        NULL);
    allocation_root = 0;
#else
    (void)type_id;
#endif
}

#ifndef NETWASM_RUNTIME_PACK
NETWASM_TEST_EXPORT("test_collect")
u32 test_collect(void)
{
    collect_internal();
    return collection_count;
}

NETWASM_TEST_EXPORT("test_collect_before_allocation")
void test_collect_before_allocation(u32 enabled)
{
    collect_before_allocation = enabled != 0;
}

NETWASM_TEST_EXPORT("test_track_allocations")
void test_track_allocations(u32 enabled)
{
    track_allocations = enabled != 0;
}

NETWASM_TEST_EXPORT("test_marked_count")
u32 test_marked_count(void)
{
    u32 result = 0;
    for (u32 index = 0; index < tracked_count; index++) {
        result += tracked_allocations[index] != 0;
    }
    return result;
}

NETWASM_TEST_EXPORT("test_clear_static_roots")
void test_clear_static_roots(void)
{
    for (u32 index = 0; index < static_root_count; index++) {
        netwasm_address_t address = static_roots[index];
        *(netwasm_reference_t *)(uintptr_t)address = 0;
        collector_unregister_root_range(
            (void *)(uintptr_t)address,
            (void *)(uintptr_t)(address + sizeof(netwasm_reference_t)));
    }
    static_root_count = 0;
}

NETWASM_TEST_EXPORT("test_allocation_count")
u32 test_allocation_count(void)
{
    return allocation_count;
}

NETWASM_TEST_EXPORT("test_tracked_allocation_count")
u32 test_tracked_allocation_count(void)
{
    return tracked_count;
}

NETWASM_TEST_EXPORT("test_collection_count")
u32 test_collection_count(void)
{
    return collection_count;
}

NETWASM_TEST_EXPORT("test_handle_count")
u32 test_handle_count(void)
{
    return strong_handle_table_count(&strong_handle_table);
}

NETWASM_TEST_EXPORT("test_fail_next_allocation")
void test_fail_next_allocation(void)
{
    fail_next_allocation = 1;
}

NETWASM_TEST_EXPORT("test_registered_type_count")
u32 test_registered_type_count(void)
{
    u32 result = 0;
    for (u32 index = 0; index < type_capacity; index++) {
        result += type_descriptors[index].registered != 0;
    }
    return result;
}

NETWASM_TEST_EXPORT("test_type_capacity")
u32 test_type_capacity(void)
{
    return type_capacity;
}

NETWASM_TEST_EXPORT("test_static_root_count")
u32 test_static_root_count(void)
{
    return static_root_count;
}

NETWASM_TEST_EXPORT("test_shadow_depth")
u32 test_shadow_depth(void)
{
    u32 depth = 0;
    uintptr_t end = root_frame_arena.cursor;
    for (FrameArenaChunk *chunk = root_frame_arena.current; chunk != NULL; chunk = chunk->previous) {
        depth += (end - (uintptr_t)frame_arena_bytes(chunk)) / sizeof(netwasm_reference_t);
        if (chunk->previous != NULL) end = chunk->previous->saved_cursor;
    }
    return depth;
}

NETWASM_TEST_EXPORT("test_exception_frame_depth")
u32 test_exception_frame_depth(void)
{
    return exception_frame_top;
}

#ifndef NETWASM_NO_FINALIZATION
NETWASM_TEST_EXPORT("test_make_finalizers_pending")
u32 test_make_finalizers_pending(void)
{
    for (u32 attempt = 0; attempt < 8; attempt++) {
        collect_internal();
        if (collector_has_pending_finalizers()) {
            return 1;
        }
    }
    return 0;
}

NETWASM_TEST_EXPORT("test_drain_finalizers")
u32 test_drain_finalizers(void)
{
    escaped_finalizer_status = 0;
    (void)collector_invoke_finalizers();
    return escaped_finalizer_status;
}

NETWASM_TEST_EXPORT("test_finalizer_count")
u32 test_finalizer_count(void)
{
    return finalizer_count;
}
#endif
#endif
static NetWasmWeakHandleTable weak_handle_table;

NETWASM_EXPORT("weak_handle_new") u32 weak_handle_new(
    netwasm_reference_t target,
    u32 track_resurrection)
{
    return weak_handle_table_create(&weak_handle_table, target, track_resurrection != 0);
}

NETWASM_EXPORT("weak_handle_get") netwasm_reference_t weak_handle_get(u32 handle)
{
    return weak_handle_table_get(&weak_handle_table, handle);
}

NETWASM_EXPORT("weak_handle_set") void weak_handle_set(
    u32 handle,
    netwasm_reference_t target)
{
    weak_handle_table_set(&weak_handle_table, handle, target);
}

NETWASM_EXPORT("weak_handle_release") void weak_handle_release(u32 handle)
{
    weak_handle_table_release(&weak_handle_table, handle);
}

static NetWasmGcHandleTable gc_handle_table;

NETWASM_EXPORT("gc_handle_new") u32 gc_handle_new(
    netwasm_reference_t target,
    u32 kind)
{
    return gc_handle_table_create(&gc_handle_table, target, kind);
}

NETWASM_EXPORT("gc_handle_get") netwasm_reference_t gc_handle_get(u32 handle)
{
    return gc_handle_table_get(&gc_handle_table, handle);
}

NETWASM_EXPORT("gc_handle_set") void gc_handle_set(
    u32 handle,
    netwasm_reference_t target)
{
    gc_handle_table_set(&gc_handle_table, handle, target);
}

NETWASM_EXPORT("gc_handle_release") void gc_handle_release(u32 handle)
{
    gc_handle_table_release(&gc_handle_table, handle);
}
NETWASM_EXPORT("gc_handle_address") netwasm_address_t gc_handle_address(u32 handle)
{
    netwasm_reference_t target = gc_handle_table_get(&gc_handle_table, handle);
    return object_data_address_resolve(
        target,
        type_capacity,
        type_data_kinds,
        NETWASM_OBJECT_HEADER_SIZE,
        NETWASM_STRING_DATA_OFFSET,
        NETWASM_ARRAY_DATA_POINTER_OFFSET);
}

NETWASM_EXPORT("gc_get_metric") uint64_t gc_get_metric(u32 metric)
{
    return gc_metric_read(metric);
}

NETWASM_EXPORT("gc_metric_is_supported") u32 gc_metric_is_supported(u32 metric)
{
    return gc_metric_supports(metric) ? 1 : 0;
}

NETWASM_EXPORT("object_identity_hash") u32 object_identity_hash(netwasm_reference_t object)
{
    return collector_identity_hash(object);
}

NETWASM_FINALIZATION_EXPORT("gc_wait_for_pending_finalizers")
void gc_wait_for_pending_finalizers(void)
{
    while (collector_has_pending_finalizers()) {
        collector_invoke_finalizers();
    }
}
