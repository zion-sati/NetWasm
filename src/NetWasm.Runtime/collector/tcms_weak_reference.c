#include "collector_weak_reference.h"
#include "tcms_runtime_state.h"

int collector_register_weak_reference(uintptr_t *slot, uintptr_t target, NetWasmWeakReferenceLifetime lifetime)
{
    return tcms_register_weak(&netwasm_tcms, slot, target, (TcmsWeakLifetime)lifetime) == TCMS_OK;
}
int collector_register_short_weak_reference(uintptr_t *slot, uintptr_t target)
{
    return collector_register_weak_reference(slot, target, NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);
}
int collector_unregister_weak_reference(uintptr_t *slot, NetWasmWeakReferenceLifetime lifetime)
{
    return tcms_unregister_weak(&netwasm_tcms, slot, (TcmsWeakLifetime)lifetime);
}
