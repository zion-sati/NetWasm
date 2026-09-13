#ifndef NETWASM_COLLECTOR_FINALIZATION_H
#define NETWASM_COLLECTOR_FINALIZATION_H

#include <stdbool.h>

typedef void (*NetWasmCollectorFinalizer)(void *object, void *context);

void collector_register_finalizer(
    void *object,
    NetWasmCollectorFinalizer finalizer,
    void *context,
    NetWasmCollectorFinalizer *previous_finalizer,
    void **previous_context);
bool collector_has_pending_finalizers(void);
int collector_invoke_finalizers(void);

#endif
