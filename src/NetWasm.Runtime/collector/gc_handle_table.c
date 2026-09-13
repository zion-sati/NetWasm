#include "gc_handle_table.h"

#include <stdlib.h>

typedef uint32_t (*gc_handle_creator)(NetWasmGcHandleTable *table, uintptr_t target);

static uint32_t create_short_weak(NetWasmGcHandleTable *table, uintptr_t target)
{
    return weak_handle_table_create(&table->weak, target, false);
}

static uint32_t create_long_weak(NetWasmGcHandleTable *table, uintptr_t target)
{
    return weak_handle_table_create(&table->weak, target, true);
}

static uint32_t create_strong(NetWasmGcHandleTable *table, uintptr_t target)
{
    return strong_handle_table_create(&table->strong, target);
}

static uint32_t create_pinned(NetWasmGcHandleTable *table, uintptr_t target)
{
    return strong_handle_table_create_pinned(&table->strong, target);
}

static const gc_handle_creator creators[] = {
    create_short_weak,
    create_long_weak,
    create_strong,
    create_pinned,
};

static uint32_t kind(uint32_t handle)
{
    return handle & 3u;
}

static uint32_t index(uint32_t handle)
{
    return handle >> 2;
}

uint32_t gc_handle_table_create(NetWasmGcHandleTable *table, uintptr_t target, uint32_t handle_kind)
{
    if (handle_kind >= sizeof(creators) / sizeof(creators[0])) return 0;
    uint32_t handle = creators[handle_kind](table, target);
    if (handle > UINT32_MAX >> 2) abort();
    return handle == 0 ? 0 : (handle << 2) | handle_kind;
}

uintptr_t gc_handle_table_get(const NetWasmGcHandleTable *table, uint32_t handle)
{
    return kind(handle) < 2
        ? weak_handle_table_get(&table->weak, index(handle))
        : strong_handle_table_get(&table->strong, index(handle));
}

void gc_handle_table_set(NetWasmGcHandleTable *table, uint32_t handle, uintptr_t target)
{
    if (kind(handle) < 2) {
        weak_handle_table_set(&table->weak, index(handle), target);
    } else {
        strong_handle_table_set(&table->strong, index(handle), target);
    }
}

void gc_handle_table_release(NetWasmGcHandleTable *table, uint32_t handle)
{
    if (kind(handle) < 2) {
        weak_handle_table_release(&table->weak, index(handle));
    } else {
        strong_handle_table_release(&table->strong, index(handle));
    }
}
