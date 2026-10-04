#include <stdint.h>

#include "native_target.h"
#include "collector/ephemeron_handle_table.h"

/* Keep these exports in a separate translation unit. Runtime packs link this
 * object only when the compiler emits an ephemeron import, and source runtime
 * builds include it only for the matching runtime feature. Moving the wrappers
 * back into native_runtime.c would retain the whole ephemeron collector in
 * applications that never use ConditionalWeakTable. */
static NetWasmEphemeronHandleTable ephemeron_handle_table;

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("ephemeron_handle_new")))
#endif
uint32_t ephemeron_handle_new(
    netwasm_reference_t key,
    netwasm_reference_t value)
{
    return ephemeron_handle_table_create(&ephemeron_handle_table, key, value);
}

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("ephemeron_handle_get_key")))
#endif
netwasm_reference_t ephemeron_handle_get_key(uint32_t handle)
{
    return ephemeron_handle_table_get_key(&ephemeron_handle_table, handle);
}

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("ephemeron_handle_get_value")))
#endif
netwasm_reference_t ephemeron_handle_get_value(uint32_t handle)
{
    return ephemeron_handle_table_get_value(&ephemeron_handle_table, handle);
}

#ifndef NETWASM_RUNTIME_PACK
__attribute__((export_name("ephemeron_handle_release")))
#endif
void ephemeron_handle_release(uint32_t handle)
{
    ephemeron_handle_table_release(&ephemeron_handle_table, handle);
}
