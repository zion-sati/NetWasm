#ifndef NETWASM_COMMAND_DIAGNOSTICS_H
#define NETWASM_COMMAND_DIAGNOSTICS_H

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include "native_target.h"
#include "collector/collector_roots.h"

/* Canonical option<list<u16>> and completion layouts. Native pointer alignment
 * matches the canonical address width for both wasm32 and wasm64. The lists
 * borrow rooted managed strings until the component's post-return operation. */
typedef struct {
    uint8_t present;
    netwasm_address_t data;
    netwasm_address_t length;
} CommandDiagnosticText;

typedef struct {
    uint32_t type_id;
    CommandDiagnosticText message;
    CommandDiagnosticText stack_trace;
} CommandDiagnosticEvent;

typedef struct {
    uint8_t failed;
    union {
        int32_t exit_code;
        CommandDiagnosticEvent event;
    } value;
} CommandDiagnosticCompletion;

/* Keep the native return area byte-for-byte compatible with the canonical ABI
 * consumed by the component adapter. These fail at compile time for both
 * supported address widths instead of leaving layout drift to host lifting. */
#if UINTPTR_MAX == UINT32_MAX
_Static_assert(sizeof(CommandDiagnosticText) == 12, "wasm32 diagnostic text layout");
_Static_assert(sizeof(CommandDiagnosticEvent) == 28, "wasm32 diagnostic event layout");
_Static_assert(sizeof(CommandDiagnosticCompletion) == 32, "wasm32 diagnostic completion layout");
_Static_assert(offsetof(CommandDiagnosticCompletion, value.event.message.data) == 12,
    "wasm32 diagnostic message address offset");
_Static_assert(offsetof(CommandDiagnosticCompletion, value.event.stack_trace.data) == 24,
    "wasm32 diagnostic stack address offset");
#elif UINTPTR_MAX == UINT64_MAX
_Static_assert(sizeof(CommandDiagnosticText) == 24, "wasm64 diagnostic text layout");
_Static_assert(sizeof(CommandDiagnosticEvent) == 56, "wasm64 diagnostic event layout");
_Static_assert(sizeof(CommandDiagnosticCompletion) == 64, "wasm64 diagnostic completion layout");
_Static_assert(offsetof(CommandDiagnosticCompletion, value.event.message.data) == 24,
    "wasm64 diagnostic message address offset");
_Static_assert(offsetof(CommandDiagnosticCompletion, value.event.stack_trace.data) == 48,
    "wasm64 diagnostic stack address offset");
#else
#error Unsupported command diagnostic address width
#endif

static CommandDiagnosticCompletion command_diagnostic_completion;
static netwasm_reference_t command_diagnostic_roots[2];
static uint32_t command_diagnostic_owned;

static void write_command_exception(
    uint32_t type_id,
    netwasm_reference_t message,
    netwasm_reference_t stack_trace)
{
    static const char header[] = "Unhandled exception #";
    static const char separator[] = ": ";
    static const char newline[] = "\n";
    /* Ten digits suffice for a uint32_t; this is a numeric-format bound, not a
     * limit on exception message or trace length. */
    char digits[10];
    size_t offset = sizeof(digits);
    uint32_t value = type_id;
    do {
        digits[--offset] = (char)('0' + value % 10);
        value /= 10;
    } while (value != 0);
    write_diagnostic_bytes(header, sizeof(header) - 1);
    write_diagnostic_bytes(digits + offset, sizeof(digits) - offset);
    if (!diagnostic_string_is_empty(message)) {
        write_diagnostic_bytes(separator, sizeof(separator) - 1);
        write_diagnostic_string(message);
    }
    write_diagnostic_bytes(newline, sizeof(newline) - 1);
    if (!diagnostic_string_is_empty(stack_trace)) {
        write_diagnostic_string(stack_trace);
        if (!diagnostic_string_ends_with_newline(stack_trace)) {
            write_diagnostic_bytes(newline, sizeof(newline) - 1);
        }
    }
}

static CommandDiagnosticText command_diagnostic_text(
    netwasm_reference_t string, uint32_t length)
{
    CommandDiagnosticText result = {0};
    if (string != 0) {
        result.present = 1;
        result.data = string + NETWASM_STRING_DATA_OFFSET;
        result.length = length;
    }
    return result;
}

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("command_exception_capture")))
#endif
void command_exception_capture(
    uint32_t type_id,
    netwasm_reference_t message,
    uint32_t message_length,
    netwasm_reference_t stack_trace,
    uint32_t stack_trace_length)
{
    if (command_diagnostic_owned || type_id == 0 || type_id > INT32_MAX) {
        abort();
    }
    command_diagnostic_roots[0] = message;
    command_diagnostic_roots[1] = stack_trace;
    collector_register_roots(command_diagnostic_roots, sizeof(command_diagnostic_roots));
    command_diagnostic_owned = 1;
    command_diagnostic_completion.failed = 1;
    command_diagnostic_completion.value.event.type_id = type_id;
    command_diagnostic_completion.value.event.message =
        command_diagnostic_text(message, message_length);
    command_diagnostic_completion.value.event.stack_trace =
        command_diagnostic_text(stack_trace, stack_trace_length);
}

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("command_exception_completion")))
#endif
netwasm_address_t command_exception_completion(int32_t exit_code)
{
    if (!command_diagnostic_owned) {
        command_diagnostic_completion.failed = 0;
        command_diagnostic_completion.value.exit_code = exit_code;
    }
    return (netwasm_address_t)(uintptr_t)&command_diagnostic_completion;
}

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("command_exception_release")))
#endif
void command_exception_release(void)
{
    if (command_diagnostic_owned) {
        collector_unregister_roots(command_diagnostic_roots, sizeof(command_diagnostic_roots));
        command_diagnostic_owned = 0;
    }
    memset(command_diagnostic_roots, 0, sizeof(command_diagnostic_roots));
    memset(&command_diagnostic_completion, 0, sizeof(command_diagnostic_completion));
}

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("command_exception_write")))
#endif
void command_exception_write(void)
{
    if (!command_diagnostic_owned) {
        abort();
    }
    write_command_exception(
        command_diagnostic_completion.value.event.type_id,
        command_diagnostic_roots[0],
        command_diagnostic_roots[1]);
    command_exception_release();
}

#endif
