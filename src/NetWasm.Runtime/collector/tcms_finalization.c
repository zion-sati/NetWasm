#include "collector_finalization.h"
#include "tcms_runtime_state.h"
#include <limits.h>
#include <stdlib.h>

void collector_register_finalizer(void *object, NetWasmCollectorFinalizer finalizer,
    void *context, NetWasmCollectorFinalizer *previous, void **previous_context)
{
    if (tcms_register_finalizer(&netwasm_tcms, (uintptr_t)object, finalizer,
        context, previous, previous_context) != TCMS_OK) abort();
}
bool collector_has_pending_finalizers(void) { return tcms_has_pending_finalizers(&netwasm_tcms); }
int collector_invoke_finalizers(void)
{
    size_t count = tcms_invoke_finalizers(&netwasm_tcms);
    return count > INT_MAX ? INT_MAX : (int)count;
}
