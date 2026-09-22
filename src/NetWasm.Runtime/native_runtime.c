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

typedef uint32_t u32;

_Static_assert(sizeof(netwasm_id_t) == sizeof(u32),
               "NetWasm semantic identifiers must remain i32");
_Static_assert(sizeof(netwasm_reference_t) == sizeof(void *),
               "A managed reference must match the selected Wasm address width");

typedef struct {
    NetWasmCollectorDescriptor descriptor;
    NetWasmCollectorDescriptor value_descriptor;
    u32 base_type_id;
    netwasm_address_t size;
    netwasm_address_t bitmap_address;
    u32 bitmap_bits;
    netwasm_address_t assignable_type_ids_address;
    u32 assignable_type_id_count;
    u32 has_finalizer;
    u32 is_interface;
    netwasm_address_t value_size;
    int value_contains_references;
    int value_registered;
    int registered;
} TypeDescriptor;

typedef struct {
    netwasm_address_t metadata_address;
    u32 clause_count;
    netwasm_address_t environment;
    u32 has_filters;
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

_Static_assert(sizeof(ExceptionClause) == 12,
               "Exception-clause metadata must remain three u32 fields");

typedef struct RootFrame {
    struct RootFrame *previous;
    u32 slot_count;
    netwasm_reference_t slots[];
} RootFrame;

typedef struct ValueFrame {
    struct ValueFrame *previous;
    u32 byte_count;
    _Alignas(16) unsigned char bytes[];
} ValueFrame;


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
static RootFrame *shadow_frame;
static u32 shadow_top;
static ValueFrame *value_frame;
static u32 value_frame_top;
static u32 *stack_trace_frames;
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
static u32 dispatch_target_frame;
static u32 dispatch_target_clause;
static u32 filter_search_floor;
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
static netwasm_reference_t dispatch_root;
static uintptr_t allocation_root;

netwasm_reference_t allocate_string(u32 value, u32 length, u32 type_id);
static NetWasmCollectorDescriptor reference_word_descriptor;
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
    status = netwasm_managed_finalize(object, type_id);
    finalizer_count++;
    if (status != 0 && escaped_finalizer_status == 0) {
        escaped_finalizer_status = status;
    }
    finalizer_root = 0;
}
#endif
static int initialized;


__attribute__((export_name("report_unobserved_task_exception")))
void report_unobserved_task_exception(void)
{
    static const char message[] =
        "NetWasm: an unobserved managed Task exception was finalized\n";
    (void)write(STDERR_FILENO, message, sizeof(message) - 1);
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
    u32 *frames;
    if (capacity < stack_trace_frame_capacity ||
        (size_t)capacity > SIZE_MAX / sizeof(u32)) {
        return 0;
    }
    frames = (u32 *)collector_allocate_metadata((size_t)capacity * sizeof(u32));
    if (frames == NULL) {
        return 0;
    }
    if (stack_trace_frame_top != 0) {
        memcpy(frames, stack_trace_frames,
               (size_t)stack_trace_frame_top * sizeof(u32));
    }
    if (stack_trace_frame_capacity != 0) {
        collector_release_metadata(stack_trace_frames);
    }
    stack_trace_frames = frames;
    stack_trace_frame_capacity = capacity;
    return 1;
}

__attribute__((export_name("stack_trace_frame_enter")))
void stack_trace_frame_enter(u32 method_id)
{
    if (method_id == 0 ||
        (stack_trace_frame_top == stack_trace_frame_capacity &&
         !grow_stack_trace_frames())) {
        __builtin_trap();
    }
    stack_trace_frames[stack_trace_frame_top++] = method_id;
}

__attribute__((export_name("stack_trace_frame_leave")))
void stack_trace_frame_leave(u32 method_id)
{
    if (method_id == 0 || stack_trace_frame_top == 0 ||
        stack_trace_frames[stack_trace_frame_top - 1] != method_id) {
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

__attribute__((export_name("stack_trace_initialize")))
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

__attribute__((export_name("stack_trace_register_symbol")))
void stack_trace_register_symbol(
    u32 method_id,
    netwasm_address_t characters,
    u32 length)
{
    if (method_id == 0 || characters == 0 || length == 0 ||
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
    if (stack_trace_symbols[method_id].characters != NULL) {
        collector_release_metadata(stack_trace_symbols[method_id].characters);
    }
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
        u32 method_id = stack_trace_frames[index - 1];
        StackTraceSymbol *symbol = method_id < stack_trace_symbol_capacity ?
            &stack_trace_symbols[method_id] : NULL;
        u32 frame_length = symbol != NULL && symbol->characters != NULL ?
            symbol->length : 7 + decimal_digit_count(method_id);
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
        u32 method_id = stack_trace_frames[index - 1];
        StackTraceSymbol *symbol = method_id < stack_trace_symbol_capacity ?
            &stack_trace_symbols[method_id] : NULL;
        if (symbol != NULL && symbol->characters != NULL) {
            memcpy(destination, symbol->characters,
                (size_t)symbol->length * sizeof(uint16_t));
            destination += symbol->length;
        } else {
            const char fallback[] = "method#";
            for (u32 character = 0; character < 7; character++) {
                *destination++ = (uint16_t)fallback[character];
            }
            destination = write_decimal(destination, method_id);
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


static void collect_internal(void)
{
    collector_collect();
    collection_count++;
}

__attribute__((export_name("collect")))
void collect(void)
{
    collect_internal();
}

static void collect_for_allocation_test(void)
{
    if (collect_before_allocation) {
        collect_internal();
    }
}

static void track(void *allocation)
{
    uintptr_t address = (uintptr_t)allocation;
    uintptr_t saved_root;
    if (!track_allocations) {
        return;
    }
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

static void enumerate_managed_roots(void)
{
    RootFrame *frame;
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
    for (frame = shadow_frame; frame != NULL; frame = frame->previous) {
        collector_trace_root_range(frame->slots, frame->slots + frame->slot_count);
    }
}

__attribute__((export_name("initialize")))
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
     * an object or array, so BDWGC must resolve that interior address back to
     * its containing allocation. This does not enable conventional stack
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
    collector_enable_interior_pointers();
    collector_disable_stack_scanning();
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
    {
        NetWasmCollectorDescriptorWord reference_bitmap[1] = {0};
        collector_mark_descriptor_word(reference_bitmap, 0);
        reference_word_descriptor = collector_create_descriptor(reference_bitmap, 1);
    }
#ifndef NETWASM_NO_FINALIZATION
    collector_enable_on_demand_finalization();
    collector_enable_java_finalization_order();
#endif
    initialized = 1;
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

__attribute__((export_name("is_assignable")))
u32 is_assignable(netwasm_reference_t object, u32 target_type_id)
{
    return object == 0
        ? 0
        : is_type_assignable(*(u32 *)(uintptr_t)object, target_type_id);
}

__attribute__((export_name("exception_frame_enter")))
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
    return exception_frame_top;
}

__attribute__((export_name("exception_frame_set_environment")))
void exception_frame_set_environment(u32 token, netwasm_address_t environment)
{
    if (token == 0 || token > exception_frame_top ||
        !exception_frames[token - 1].has_filters) {
        __builtin_trap();
    }
    exception_frames[token - 1].environment = environment;
}

__attribute__((export_name("exception_frame_leave")))
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

__attribute__((export_name("exception_frame_target_clause")))
u32 exception_frame_target_clause(u32 token)
{
    return token == dispatch_target_frame ? dispatch_target_clause : 0;
}

static void dispatch_exception(netwasm_reference_t exception)
{
    dispatch_root = exception;
    dispatch_target_frame = 0;
    dispatch_target_clause = 0;
    for (u32 token = exception_frame_top; token > filter_search_floor; token--) {
        ExceptionFrame *frame = &exception_frames[token - 1];
        if (!frame->has_filters) {
            const u32 *catch_types =
                (const u32 *)(uintptr_t)frame->metadata_address;
            for (u32 clause = 0; clause < frame->clause_count; clause++) {
                if (is_assignable(exception, catch_types[clause])) {
                    dispatch_target_frame = token;
                    dispatch_target_clause = clause + 1;
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
                u32 saved_frame = dispatch_target_frame;
                u32 saved_clause = dispatch_target_clause;
                u32 saved_floor = filter_search_floor;
                filter_search_floor = token;
                u32 accepted = netwasm_evaluate_filter(
                    clauses[clause].value,
                    exception,
                    frame->environment);
                filter_search_floor = saved_floor;
                dispatch_root = saved_root;
                dispatch_target_frame = saved_frame;
                dispatch_target_clause = saved_clause;
                if (!accepted) {
                    continue;
                }
            }
            {
                dispatch_target_frame = token;
                dispatch_target_clause = clause + 1;
                return;
            }
        }
    }
}

__attribute__((export_name("begin_throw")))
void begin_throw(netwasm_reference_t exception)
{
    capture_stack_trace(exception);
    dispatch_exception(exception);
}

__attribute__((export_name("begin_rethrow")))
void begin_rethrow(netwasm_reference_t exception)
{
    dispatch_exception(exception);
}

__attribute__((export_name("end_catch")))
void end_catch(void)
{
    dispatch_root = 0;
    dispatch_target_frame = 0;
    dispatch_target_clause = 0;
}

__attribute__((export_name("exception_get_active")))
netwasm_reference_t exception_get_active(void)
{
    return dispatch_root;
}

__attribute__((export_name("exception_get_active_type_id")))
u32 exception_get_active_type_id(void)
{
    if (dispatch_root == 0) {
        return 0;
    }
    return *(const u32 *)(uintptr_t)dispatch_root;
}

__attribute__((export_name("exception_clear_active")))
void exception_clear_active(void)
{
    end_catch();
#ifndef NETWASM_NO_FINALIZATION
    escaped_finalizer_status = 0;
#endif
}

__attribute__((export_name("finalizer_safepoint")))
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

__attribute__((export_name("register_type")))
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
    entry->descriptor = collector_create_descriptor(
        (const NetWasmCollectorDescriptorWord *)(uintptr_t)bitmap_address,
        bitmap_bits);
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

__attribute__((export_name("register_value_type")))
void register_value_type(
    u32 type_id,
    netwasm_address_t size,
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
    entry->value_descriptor = collector_create_descriptor(
        (const NetWasmCollectorDescriptorWord *)(uintptr_t)bitmap_address,
        bitmap_bits);
    entry->value_size = size;
    entry->value_contains_references = 0;
    for (u32 bit = 0; bitmap_address != 0 && bit < bitmap_bits; bit++) {
        const NetWasmCollectorDescriptorWord *bitmap = (const NetWasmCollectorDescriptorWord *)(uintptr_t)bitmap_address;
        if ((bitmap[bit / (sizeof(NetWasmCollectorDescriptorWord) * 8)] &
             ((NetWasmCollectorDescriptorWord)1 << (bit % (sizeof(NetWasmCollectorDescriptorWord) * 8)))) != 0) {
            entry->value_contains_references = 1;
        }
    }
    entry->value_registered = 1;
}

__attribute__((export_name("register_static_root")))
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

__attribute__((export_name("handle_new"))) u32 handle_new(netwasm_reference_t target)
{
    return strong_handle_table_create(&strong_handle_table, target);
}

__attribute__((export_name("handle_get"))) netwasm_reference_t handle_get(u32 handle)
{
    return strong_handle_table_get(&strong_handle_table, handle);
}

__attribute__((export_name("handle_release"))) void handle_release(u32 handle)
{
    strong_handle_table_release(&strong_handle_table, handle);
}


__attribute__((export_name("root_frame_enter")))
netwasm_address_t root_frame_enter(u32 slot_count)
{
    size_t slot_bytes;
    RootFrame *frame;
    if ((size_t)slot_count >
        (SIZE_MAX - sizeof(RootFrame)) / sizeof(netwasm_reference_t)) {
        return 0;
    }
    slot_bytes = (size_t)slot_count * sizeof(netwasm_reference_t);
    frame = (RootFrame *)collector_allocate_metadata(
        sizeof(RootFrame) + slot_bytes);
    if (frame == NULL) {
        return 0;
    }
    frame->previous = shadow_frame;
    frame->slot_count = slot_count;
    memset(frame->slots, 0, slot_bytes);
    shadow_frame = frame;
    shadow_top += slot_count;
    return (netwasm_address_t)(uintptr_t)frame->slots;
}

__attribute__((export_name("root_frame_leave")))
void root_frame_leave(netwasm_address_t frame)
{
    RootFrame *current = shadow_frame;
    netwasm_reference_t *slots = (netwasm_reference_t *)(uintptr_t)frame;
    if (current == NULL || slots != current->slots) {
        __builtin_trap();
    }
    shadow_top -= current->slot_count;
    shadow_frame = current->previous;
    collector_release_metadata(current);
}

__attribute__((export_name("value_frame_enter")))
netwasm_address_t value_frame_enter(netwasm_address_t byte_count)
{
    const netwasm_address_t alignment = 16u;
    netwasm_address_t aligned_count;
    ValueFrame *frame;
    if (byte_count > UINT32_MAX - (alignment - 1u) ||
        byte_count > SIZE_MAX - (alignment - 1u)) {
        return 0;
    }
    aligned_count = (byte_count + alignment - 1u) & ~(alignment - 1u);
    if ((size_t)aligned_count > SIZE_MAX - sizeof(ValueFrame)) {
        return 0;
    }
    frame = (ValueFrame *)collector_allocate_metadata(
        sizeof(ValueFrame) + (size_t)aligned_count);
    if (frame == NULL) {
        return 0;
    }
    frame->previous = value_frame;
    frame->byte_count = (u32)aligned_count;
    memset(frame->bytes, 0, (size_t)aligned_count);
    value_frame = frame;
    value_frame_top += (u32)aligned_count;
    return (netwasm_address_t)(uintptr_t)frame->bytes;
}

__attribute__((export_name("value_frame_leave")))
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
    ValueFrame *previous = target->previous;
    do {
        ValueFrame *current = value_frame;
        value_frame_top -= current->byte_count;
        value_frame = current->previous;
        collector_release_metadata(current);
    } while (value_frame != previous);
}

__attribute__((export_name("allocate")))
netwasm_reference_t allocate(netwasm_address_t size, u32 type_id)
{
    void *allocation;
    collect_for_allocation_test();
    if (fail_next_allocation) {
        fail_next_allocation = 0;
        return 0;
    }
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        size != type_descriptors[type_id].size) {
        __builtin_trap();
    }
    allocation = collector_allocate_exact(
        size,
        type_descriptors[type_id].descriptor);
    if (allocation == NULL) {
        return 0;
    }
    ((u32 *)allocation)[0] = type_id;
    allocation_root = (uintptr_t)allocation;
#ifndef NETWASM_NO_FINALIZATION
    if (type_descriptors[type_id].has_finalizer) {
        collector_register_finalizer(
            allocation,
            managed_finalizer,
            (void *)(uintptr_t)type_id,
            NULL,
            NULL);
    }
#endif
    allocation_count++;
    track(allocation);
    allocation_root = 0;
type_data_kinds[type_id] = NETWASM_OBJECT_DATA_FIELDS;
    return (netwasm_reference_t)(uintptr_t)allocation;
}

static int is_power_of_two(netwasm_address_t value)
{
    return value != 0 && (value & (value - 1)) == 0;
}

__attribute__((export_name("component_free")))
void component_free(netwasm_address_t address)
{
    ComponentAllocationHeader *header;
    if (address == 0) {
        return;
    }
    if (component_active_allocation_count == 0) {
        __builtin_trap();
    }
    header = (ComponentAllocationHeader *)(uintptr_t)address - 1;
    component_active_allocation_count--;
        netwasm_unmanaged_free(header->allocation);
}

__attribute__((export_name("component_realloc")))
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
    if (fail_next_component_allocation) {
        fail_next_component_allocation = 0;
        return 0;
    }
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
    component_active_allocation_count++;
    if (old_size != 0) {
        copy_size = old_size < new_size ? (size_t)old_size : (size_t)new_size;
        memcpy((void *)(uintptr_t)aligned_address,
               (const void *)(uintptr_t)old_address,
               copy_size);
        component_free(old_address);
    }
    return aligned_address;
}

__attribute__((export_name("native_alloc")))
netwasm_address_t native_alloc(netwasm_address_t size)
{
    return component_realloc(0, 0, sizeof(void *), size == 0 ? 1 : size);
}

__attribute__((export_name("native_realloc")))
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

__attribute__((export_name("native_free")))
void native_free(netwasm_address_t address)
{
    component_free(address);
}

__attribute__((export_name("native_aligned_alloc")))
netwasm_address_t native_aligned_alloc(
    netwasm_address_t size,
    netwasm_address_t alignment)
{
    return component_realloc(0, 0, alignment, size == 0 ? 1 : size);
}

__attribute__((export_name("native_aligned_realloc")))
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

__attribute__((export_name("native_aligned_free")))
void native_aligned_free(netwasm_address_t address)
{
    component_free(address);
}

__attribute__((export_name("test_component_active_allocation_count")))
u32 test_component_active_allocation_count(void)
{
    return component_active_allocation_count;
}

__attribute__((export_name("test_fail_next_component_allocation")))
void test_fail_next_component_allocation(void)
{
    fail_next_component_allocation = 1;
}

__attribute__((export_name("get_type_object")))
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

__attribute__((export_name("allocate_reference_array")))
netwasm_reference_t allocate_reference_array(
    u32 length,
    u32 type_id,
    u32 element_type_id)
{
    netwasm_reference_t array;
    void *elements = NULL;
    if (type_id >= type_capacity || !type_descriptors[type_id].registered ||
        type_descriptors[type_id].size != NETWASM_ARRAY_OBJECT_SIZE ||
        element_type_id >= type_capacity || !type_descriptors[element_type_id].registered) {
        __builtin_trap();
    }
    if ((size_t)length > SIZE_MAX / sizeof(netwasm_reference_t)) {
        return 0;
    }
    collect_for_allocation_test();
    if (length != 0) {
        elements = collector_allocate_exact_zeroed(
            length,
            sizeof(netwasm_reference_t),
            reference_word_descriptor);
        if (elements == NULL) {
            return 0;
        }
        allocation_root = (uintptr_t)elements;
    }
    array = (netwasm_reference_t)(uintptr_t)collector_allocate_exact(
        NETWASM_ARRAY_OBJECT_SIZE,
        type_descriptors[type_id].descriptor);
    if (array == 0) {
        allocation_root = 0;
        return 0;
    }
    *(u32 *)(uintptr_t)array = type_id;
    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_LENGTH_OFFSET) = length;
    *(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_ARRAY_DATA_POINTER_OFFSET) =
        (netwasm_reference_t)(uintptr_t)elements;
    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET) =
        element_type_id;
    allocation_root = array;
    allocation_count += length == 0 ? 1 : 2;
    track((void *)(uintptr_t)array);
    if (elements != NULL) {
        track(elements);
    }
    allocation_root = 0;
type_data_kinds[type_id] = NETWASM_OBJECT_DATA_ARRAY;
    return array;
}

__attribute__((export_name("allocate_value_array")))
netwasm_reference_t allocate_value_array(
    u32 length,
    u32 type_id,
    u32 element_type_id,
    u32 element_size)
{
    TypeDescriptor *element;
    netwasm_reference_t array;
    void *elements = NULL;
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
    if (length != 0) {
        size_t size = (size_t)length * element->value_size;
        if (element->value_contains_references) {
            elements = collector_allocate_exact_zeroed(
                length,
                element->value_size,
                element->value_descriptor);
        } else {
            elements = collector_allocate_atomic(size);
            if (elements != NULL) {
                memset(elements, 0, size);
            }
        }
        if (elements == NULL) {
            return 0;
        }
        allocation_root = (uintptr_t)elements;
    }
    array = (netwasm_reference_t)(uintptr_t)collector_allocate_exact(
        NETWASM_ARRAY_OBJECT_SIZE,
        type_descriptors[type_id].descriptor);
    if (array == 0) {
        allocation_root = 0;
        return 0;
    }
    *(u32 *)(uintptr_t)array = type_id;
    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_LENGTH_OFFSET) = length;
    *(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_ARRAY_DATA_POINTER_OFFSET) =
        (netwasm_reference_t)(uintptr_t)elements;
    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET) =
        element_type_id;
    allocation_root = array;
    allocation_count += length == 0 ? 1 : 2;
    track((void *)(uintptr_t)array);
    if (elements != NULL) {
        track(elements);
    }
    allocation_root = 0;
type_data_kinds[type_id] = NETWASM_OBJECT_DATA_ARRAY;
    return array;
}

__attribute__((export_name("allocate_rectangular_array")))
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
    void *elements = NULL;
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
    array = (netwasm_reference_t)(uintptr_t)collector_allocate_exact(
        NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE,
        type_descriptors[type_id].descriptor);
    if (array == 0) {
        return 0;
    }
    *(u32 *)(uintptr_t)array = type_id;
    allocation_root = array;

    shape = (u32 *)collector_allocate_atomic(
        (size_t)rank * NETWASM_RECTANGULAR_ARRAY_DIMENSION_SIZE);
    if (shape == NULL) {
        allocation_root = 0;
        return 0;
    }
    *(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET) =
        (netwasm_reference_t)(uintptr_t)shape;
    for (u32 dimension = rank; dimension != 0; dimension--) {
        u32 index = dimension - 1;
        shape[index * 2] = dimensions[index];
        shape[index * 2 + 1] = stride;
        stride *= dimensions[index];
    }

    if (total != 0) {
        if (elements_are_references != 0) {
            elements = collector_allocate_exact_zeroed(
                total,
                sizeof(netwasm_reference_t),
                reference_word_descriptor);
        } else if (element->value_contains_references) {
            elements = collector_allocate_exact_zeroed(
                total,
                element->value_size,
                element->value_descriptor);
        } else {
            size_t size = (size_t)total * element->value_size;
            elements = collector_allocate_atomic(size);
            if (elements != NULL) {
                memset(elements, 0, size);
            }
        }
        if (elements == NULL) {
            allocation_root = 0;
            return 0;
        }
    }

    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_LENGTH_OFFSET) = total;
    *(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_ARRAY_DATA_POINTER_OFFSET) =
        (netwasm_reference_t)(uintptr_t)elements;
    *(u32 *)(uintptr_t)(array + NETWASM_ARRAY_ELEMENT_TYPE_ID_OFFSET) =
        element_type_id;
    *(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET) = rank;
    allocation_count += total == 0 ? 2 : 3;
    track((void *)(uintptr_t)array);
    track(shape);
    if (elements != NULL) {
        track(elements);
    }
    allocation_root = 0;
type_data_kinds[type_id] = NETWASM_OBJECT_DATA_ARRAY;
    return array;
}

__attribute__((export_name("array_rank")))
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
        ? *(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET)
        : 1;
}

__attribute__((export_name("array_get_length")))
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
    rank = *(u32 *)(uintptr_t)(array + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET);
    if (dimension < 0 || (u32)dimension >= rank) {
        return -1;
    }
    shape = (u32 *)(uintptr_t)*(netwasm_reference_t *)(uintptr_t)(
        array + NETWASM_RECTANGULAR_ARRAY_SHAPE_POINTER_OFFSET);
    return (int32_t)shape[(u32)dimension * 2];
}

__attribute__((export_name("array_copy")))
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
        ARRAY_COPY_ELEMENT_CAST_FAILURE = 2
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
    if (!is_type_assignable(source_type_id, destination_type_id) &&
        !is_type_assignable(destination_type_id, source_type_id) &&
        !source_element->is_interface && !destination_element->is_interface) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    if (source_element->value_registered != destination_element->value_registered) {
        return ARRAY_COPY_TYPE_MISMATCH;
    }
    if (source_element->value_registered &&
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
        source_element->value_size,
        destination_element->value_descriptor);
    return ARRAY_COPY_SUCCESS;
}

__attribute__((export_name("array_clone")))
netwasm_reference_t array_clone(netwasm_reference_t source)
{
    u32 type_id;
    u32 element_type_id;
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
    length = *(u32 *)(uintptr_t)(source + NETWASM_ARRAY_LENGTH_OFFSET);

    if (type_descriptors[type_id].size == NETWASM_RECTANGULAR_ARRAY_OBJECT_SIZE) {
        u32 rank = *(u32 *)(uintptr_t)(
            source + NETWASM_RECTANGULAR_ARRAY_RANK_OFFSET);
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
            collector_store_reference(clone, &clone_values[index], source_values[index]);
        }
    } else {
        collector_move_value_range(
            clone,
            (void *)(uintptr_t)clone_data,
            (const void *)(uintptr_t)source_data,
            length,
            element->value_size,
            element->value_descriptor);
    }
    return clone;
}

__attribute__((export_name("array_clear")))
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
        element->value_size,
        element->value_descriptor);
}

__attribute__((export_name("allocate_string")))
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
    if (fail_next_allocation) {
        fail_next_allocation = 0;
        return 0;
    }
    byte_count = NETWASM_STRING_DATA_OFFSET + length * sizeof(uint16_t);
    string = (netwasm_reference_t)(uintptr_t)collector_allocate_atomic(byte_count);
    if (string == 0) {
        return 0;
    }
    *(u32 *)(uintptr_t)string = type_id;
    *(u32 *)(uintptr_t)(string + NETWASM_STRING_LENGTH_OFFSET) = length;
    characters = (uint16_t *)(uintptr_t)(string + NETWASM_STRING_DATA_OFFSET);
    for (u32 index = 0; index < length; index++) {
        characters[index] = (uint16_t)value;
    }
    allocation_root = string;
    allocation_count++;
    track((void *)(uintptr_t)string);
    allocation_root = 0;
type_data_kinds[type_id] = NETWASM_OBJECT_DATA_STRING;
    return string;
}

__attribute__((export_name("suppress_finalize")))
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

__attribute__((export_name("reregister_for_finalize")))
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

__attribute__((export_name("test_collect")))
u32 test_collect(void)
{
    collect_internal();
    return collection_count;
}

__attribute__((export_name("test_collect_before_allocation")))
void test_collect_before_allocation(u32 enabled)
{
    collect_before_allocation = enabled != 0;
}

__attribute__((export_name("test_track_allocations")))
void test_track_allocations(u32 enabled)
{
    track_allocations = enabled != 0;
}

__attribute__((export_name("test_marked_count")))
u32 test_marked_count(void)
{
    u32 result = 0;
    for (u32 index = 0; index < tracked_count; index++) {
        result += tracked_allocations[index] != 0;
    }
    return result;
}

__attribute__((export_name("test_clear_static_roots")))
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

__attribute__((export_name("test_allocation_count")))
u32 test_allocation_count(void)
{
    return allocation_count;
}

__attribute__((export_name("test_tracked_allocation_count")))
u32 test_tracked_allocation_count(void)
{
    return tracked_count;
}

__attribute__((export_name("test_collection_count")))
u32 test_collection_count(void)
{
    return collection_count;
}

__attribute__((export_name("test_handle_count")))
u32 test_handle_count(void)
{
    return strong_handle_table_count(&strong_handle_table);
}

__attribute__((export_name("test_fail_next_allocation")))
void test_fail_next_allocation(void)
{
    fail_next_allocation = 1;
}

__attribute__((export_name("test_registered_type_count")))
u32 test_registered_type_count(void)
{
    u32 result = 0;
    for (u32 index = 0; index < type_capacity; index++) {
        result += type_descriptors[index].registered != 0;
    }
    return result;
}

__attribute__((export_name("test_type_capacity")))
u32 test_type_capacity(void)
{
    return type_capacity;
}

__attribute__((export_name("test_static_root_count")))
u32 test_static_root_count(void)
{
    return static_root_count;
}

__attribute__((export_name("test_shadow_depth")))
u32 test_shadow_depth(void)
{
    return shadow_top;
}

__attribute__((export_name("test_exception_frame_depth")))
u32 test_exception_frame_depth(void)
{
    return exception_frame_top;
}

#ifndef NETWASM_NO_FINALIZATION
__attribute__((export_name("test_make_finalizers_pending")))
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

__attribute__((export_name("test_drain_finalizers")))
u32 test_drain_finalizers(void)
{
    escaped_finalizer_status = 0;
    (void)collector_invoke_finalizers();
    return escaped_finalizer_status;
}

__attribute__((export_name("test_finalizer_count")))
u32 test_finalizer_count(void)
{
    return finalizer_count;
}
#endif
static NetWasmWeakHandleTable weak_handle_table;

__attribute__((export_name("weak_handle_new"))) u32 weak_handle_new(
    netwasm_reference_t target,
    u32 track_resurrection)
{
    return weak_handle_table_create(&weak_handle_table, target, track_resurrection != 0);
}

__attribute__((export_name("weak_handle_get"))) netwasm_reference_t weak_handle_get(u32 handle)
{
    return weak_handle_table_get(&weak_handle_table, handle);
}

__attribute__((export_name("weak_handle_set"))) void weak_handle_set(
    u32 handle,
    netwasm_reference_t target)
{
    weak_handle_table_set(&weak_handle_table, handle, target);
}

__attribute__((export_name("weak_handle_release"))) void weak_handle_release(u32 handle)
{
    weak_handle_table_release(&weak_handle_table, handle);
}

static NetWasmGcHandleTable gc_handle_table;

__attribute__((export_name("gc_handle_new"))) u32 gc_handle_new(
    netwasm_reference_t target,
    u32 kind)
{
    return gc_handle_table_create(&gc_handle_table, target, kind);
}

__attribute__((export_name("gc_handle_get"))) netwasm_reference_t gc_handle_get(u32 handle)
{
    return gc_handle_table_get(&gc_handle_table, handle);
}

__attribute__((export_name("gc_handle_set"))) void gc_handle_set(
    u32 handle,
    netwasm_reference_t target)
{
    gc_handle_table_set(&gc_handle_table, handle, target);
}

__attribute__((export_name("gc_handle_release"))) void gc_handle_release(u32 handle)
{
    gc_handle_table_release(&gc_handle_table, handle);
}
__attribute__((export_name("gc_handle_address"))) netwasm_address_t gc_handle_address(u32 handle)
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

__attribute__((export_name("gc_get_metric"))) uint64_t gc_get_metric(u32 metric)
{
    return gc_metric_read(metric);
}

__attribute__((export_name("gc_metric_is_supported"))) u32 gc_metric_is_supported(u32 metric)
{
    return gc_metric_supports(metric) ? 1 : 0;
}

__attribute__((export_name("object_identity_hash"))) u32 object_identity_hash(netwasm_reference_t object)
{
    return collector_identity_hash(object);
}

__attribute__((export_name("gc_wait_for_pending_finalizers"))) void gc_wait_for_pending_finalizers(void)
{
    while (collector_has_pending_finalizers()) {
        collector_invoke_finalizers();
    }
}
