#include "collector_ephemeron.h"
#include "tcms_runtime_state.h"

int collector_register_ephemeron(uintptr_t *key, uintptr_t *value)
{
    return tcms_register_ephemeron(&netwasm_tcms, key, value) == TCMS_OK;
}
void collector_unregister_ephemeron(uintptr_t *key, uintptr_t *value)
{
    (void)tcms_unregister_ephemeron(&netwasm_tcms, key, value);
}
