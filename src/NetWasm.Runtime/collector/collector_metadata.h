#ifndef NETWASM_COLLECTOR_METADATA_H
#define NETWASM_COLLECTOR_METADATA_H

#include <stddef.h>

/* Explicitly owned, zero-initialized storage; allocation must not collect
   managed objects or change the caller's collection-disable state. */
void *collector_allocate_metadata(size_t size);
void collector_release_metadata(void *address);

#endif
