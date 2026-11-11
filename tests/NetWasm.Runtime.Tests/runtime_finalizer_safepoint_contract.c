#include "runtime_finalizer_safepoint.h"

#include "collector/collector_collection.h"
#include "collector/collector_finalization.h"

#include <assert.h>
#include <stdlib.h>

static int queued;
static int drains;
static int reentries;

bool collector_has_pending_finalizers(void)
{
    return queued != 0;
}

int collector_invoke_finalizers(void)
{
    drains++;
    /* A finalizer that calls back into the module reaches another safepoint. */
    if (reentries > 0) {
        reentries--;
        runtime_run_queued_finalizers();
    }
    int ran = queued;
    queued = 0;
    return ran;
}

/* A safepoint never collects: allocation paces collections. */
void collector_collect(void)
{
    abort();
}

int main(void)
{
    runtime_run_queued_finalizers();
    assert(drains == 0);

    queued = 2;
    runtime_run_queued_finalizers();
    assert(drains == 1 && queued == 0);
    runtime_run_queued_finalizers();
    assert(drains == 1);

    queued = 1;
    reentries = 1;
    runtime_run_queued_finalizers();
    assert(drains == 2 && queued == 0 && reentries == 0);

    queued = 1;
    runtime_run_queued_finalizers();
    assert(drains == 3 && queued == 0);
    return 0;
}
