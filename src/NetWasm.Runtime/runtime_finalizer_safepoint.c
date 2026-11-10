#include "runtime_finalizer_safepoint.h"

#include "collector/collector_finalization.h"

#include <stdbool.h>

static bool running;

/* A safepoint runs the finalizers a collection has already queued and never collects:
 * allocation paces collections, as on Mono, so a call into a module costs nothing more when
 * no finalizer waits, however much the module keeps alive. A finalizer that calls back into
 * the module reaches another safepoint, which leaves the queue to the drain already running. */
void runtime_run_queued_finalizers(void)
{
    if (running || !collector_has_pending_finalizers()) {
        return;
    }
    running = true;
    (void)collector_invoke_finalizers();
    running = false;
}
