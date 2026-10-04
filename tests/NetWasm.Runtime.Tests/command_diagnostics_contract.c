#include <assert.h>
#include <setjmp.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include "native_target.h"

static jmp_buf failure;
static unsigned registrations;
static unsigned unregistrations;
static netwasm_reference_t *retained_roots;
static char output[32768];
static size_t output_length;

static _Noreturn void command_contract_abort(void) { longjmp(failure, 1); }
static void write_diagnostic_bytes(const char *value, size_t length)
{
    assert(output_length + length < sizeof(output));
    memcpy(output + output_length, value, length);
    output_length += length;
    output[output_length] = 0;
}
static int diagnostic_string_is_empty(netwasm_reference_t string)
{
    return string == 0 || *(uint32_t *)(string + NETWASM_STRING_LENGTH_OFFSET) == 0;
}
static int diagnostic_string_ends_with_newline(netwasm_reference_t string)
{
    uint32_t length = *(uint32_t *)(string + NETWASM_STRING_LENGTH_OFFSET);
    return ((uint16_t *)(string + NETWASM_STRING_DATA_OFFSET))[length - 1] == '\n';
}
static void write_diagnostic_string(netwasm_reference_t string)
{
    uint32_t length = *(uint32_t *)(string + NETWASM_STRING_LENGTH_OFFSET);
    uint16_t *text = (uint16_t *)(string + NETWASM_STRING_DATA_OFFSET);
    for (uint32_t index = 0; index < length; index++) {
        char value = (char)text[index];
        write_diagnostic_bytes(&value, 1);
    }
}
void collector_register_roots(void *start, size_t size)
{
    assert(retained_roots == NULL);
    assert(size == 2 * sizeof(netwasm_reference_t));
    retained_roots = start;
    registrations++;
}
void collector_unregister_roots(void *start, size_t size)
{
    assert(start == retained_roots);
    assert(size == 2 * sizeof(netwasm_reference_t));
    retained_roots = NULL;
    unregistrations++;
}
#pragma GCC diagnostic push
/* export_name is meaningful only in the actual Wasm builds; this native
 * contract exercises the same value ownership implementation in memory. */
#pragma GCC diagnostic ignored "-Wattributes"
#define abort command_contract_abort
#include "command_diagnostics.h"
#undef abort
#pragma GCC diagnostic pop

struct TestString {
    netwasm_reference_t type;
    uint32_t length;
    uint16_t text[8];
};

int main(void)
{
    struct TestString empty = {1, 0, {0}};
    struct TestString message = {1, 5, {'h', 'e', 'l', 'l', 'o'}};
    struct TestString trace = {1, 2, {'a', 't'}};
    struct TestString newline_trace = {1, 3, {'a', 't', '\n'}};
    struct TestString unicode = {1, 3, {0xd800, 0x41, 0xdc00}};
    CommandDiagnosticCompletion *result = (void *)command_exception_completion(1);
    assert(result->failed == 0 && result->value.exit_code == 1);
    assert(registrations == 0);
    command_exception_release();

    command_exception_capture(7, 0, 0, 0, 0);
    result = (void *)command_exception_completion(0);
    assert(result->failed == 1 && result->value.event.type_id == 7);
    assert(result->value.event.message.present == 0 && result->value.event.message.data == 0);
    assert(result->value.event.stack_trace.present == 0);
    assert(retained_roots[0] == 0 && retained_roots[1] == 0);
    command_exception_write();
    assert(strcmp(output, "Unhandled exception #7\n") == 0);
    assert(retained_roots == NULL && registrations == unregistrations);

    command_exception_capture(8, (uintptr_t)&empty, 0, (uintptr_t)&unicode, 3);
    result = (void *)command_exception_completion(0);
    assert(result->value.event.message.present == 1 && result->value.event.message.length == 0);
    assert(result->value.event.message.data == (uintptr_t)empty.text);
    assert(result->value.event.stack_trace.present == 1 && result->value.event.stack_trace.length == 3);
    assert(memcmp((void *)result->value.event.stack_trace.data, unicode.text, 6) == 0);
    assert(retained_roots[0] == (uintptr_t)&empty && retained_roots[1] == (uintptr_t)&unicode);
    command_exception_release();
    assert(retained_roots == NULL && result->failed == 0);
    assert(command_diagnostic_roots[0] == 0 && command_diagnostic_roots[1] == 0);

    output_length = 0;
    command_exception_capture(INT32_MAX, (uintptr_t)&message, 5, (uintptr_t)&trace, 2);
    command_exception_write();
    assert(strcmp(output, "Unhandled exception #2147483647: hello\nat\n") == 0);
    output_length = 0;
    command_exception_capture(7, (uintptr_t)&message, 5, (uintptr_t)&newline_trace, 3);
    command_exception_write();
    assert(strcmp(output, "Unhandled exception #7: hello\nat\n") == 0);
    command_exception_capture(7, (uintptr_t)&empty, 0, (uintptr_t)&empty, 0);
    command_exception_release();

    if (setjmp(failure) == 0) { command_exception_capture(0, 0, 0, 0, 0); assert(0); }
    if (setjmp(failure) == 0) { command_exception_capture(UINT32_MAX, 0, 0, 0, 0); assert(0); }
    command_exception_capture(7, 0, 0, 0, 0);
    if (setjmp(failure) == 0) { command_exception_capture(8, 0, 0, 0, 0); assert(0); }
    assert(((CommandDiagnosticCompletion *)command_exception_completion(0))->value.event.type_id == 7);
    command_exception_release();
    if (setjmp(failure) == 0) { command_exception_write(); assert(0); }
    assert(registrations == unregistrations);
    return 0;
}
