#ifndef NETWASM_TCMS_RUNTIME_STATE_H
#define NETWASM_TCMS_RUNTIME_STATE_H
#include "tcms.h"
#include "tcms_lifetime.h"

extern Tcms netwasm_tcms;
extern uint64_t netwasm_tcms_collections;
extern uint64_t netwasm_tcms_allocated_bytes;
void netwasm_tcms_trace_roots(Tcms *, void *);
#endif
