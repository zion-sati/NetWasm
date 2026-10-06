#include "collector_collection.h"
#include "tcms_runtime_state.h"

void collector_collect(void) { tcms_collect(&netwasm_tcms, NULL, 0); }
uint64_t collector_collection_count(void) { return netwasm_tcms_collections; }
uint64_t collector_total_allocated_bytes(void) { return netwasm_tcms_allocated_bytes; }
uint64_t collector_heap_size(void) { return netwasm_tcms.bytes; }
/* There is no separately reserved managed free region; malloc owns free space. */
uint64_t collector_free_bytes(void) { return 0; }
bool collector_try_read_total_pause_milliseconds(uint64_t *value) { (void)value; return false; }
