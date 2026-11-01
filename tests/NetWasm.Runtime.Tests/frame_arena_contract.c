#include <assert.h>
#include <stdbool.h>

/* Include the production implementation so owner state and exact root
 * enumeration are tested directly. Unused runtime entry points are discarded
 * by the contract link, not replaced by test implementations. */
#define NETWASM_RUNTIME_PACK
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wattributes"
#include "../../src/NetWasm.Runtime/native_runtime.c"
#pragma GCC diagnostic pop

static bool fail_metadata;
static size_t metadata_allocations, metadata_releases, observed_roots;
static uintptr_t observed_sum;

void *collector_allocate_metadata(size_t size)
{
    if (fail_metadata || size > 1024 * 1024) return NULL;
    void *allocation = calloc(1, size + sizeof(void *) + 31);
    assert(allocation != NULL);
    /* Exercise the actual minimum Wasm alignment even on native allocators
       that happen to provide sixteen bytes. Preserve the true free address. */
    uintptr_t address = ((uintptr_t)allocation + sizeof(void *) + 15) & ~(uintptr_t)15;
    address += 8;
    void *result = (void *)address;
    ((void **)result)[-1] = allocation;
    metadata_allocations++;
    return result;
}

void collector_release_metadata(void *address)
{
    metadata_releases++;
    free(((void **)address)[-1]);
}

void collector_trace_root_range(void *start, void *end)
{
    for (netwasm_reference_t *slot = start; slot < (netwasm_reference_t *)end; slot++) {
        if (*slot == 0) continue;
        observed_roots++;
        observed_sum += *slot;
    }
}

static void verify_roots(size_t count, uintptr_t sum)
{
    observed_roots = 0;
    observed_sum = 0;
    enumerate_managed_roots();
    assert(observed_roots == count && observed_sum == sum);
}

static size_t root_slot_count(void)
{
    size_t count = 0;
    uintptr_t end = root_frame_arena.cursor;
    for (FrameArenaChunk *chunk = root_frame_arena.current; chunk != NULL; chunk = chunk->previous) {
        count += (end - (uintptr_t)frame_arena_bytes(chunk)) / sizeof(netwasm_reference_t);
        if (chunk->previous != NULL) end = chunk->previous->saved_cursor;
    }
    return count;
}

static void release_arena(FrameArena *arena)
{
    FrameArenaChunk *chunk = arena->current;
    while (chunk != NULL) {
        FrameArenaChunk *next = chunk->next;
        collector_release_metadata(chunk);
        chunk = next;
    }
    arena->current = NULL;
    arena->cursor = arena->limit = arena->begin = 0;
}

static void root_frames(void)
{
    fail_metadata = true;
    assert(root_frame_enter(1) == 0 && root_slot_count() == 0 && root_frame_arena.current == NULL);
    fail_metadata = false;
    netwasm_address_t frames[96];
    uintptr_t sum = 0;
    for (size_t i = 0; i < 96; i++) {
        frames[i] = root_frame_enter(17);
        assert(frames[i] != 0);
        assert(frames[i] >= root_frame_arena.begin && frames[i] < root_frame_arena.cursor);
        assert(root_slot_count() == (i + 1) * 17);
        netwasm_reference_t *slots = (void *)(uintptr_t)frames[i];
        for (size_t j = 0; j < 17; j++) slots[j] = i * 17 + j + 1;
        sum += (i * 17 + 1 + i * 17 + 17) * 17 / 2;
        verify_roots((i + 1) * 17, sum);
    }
    FrameArenaChunk *current = root_frame_arena.current;
    uintptr_t cursor = root_frame_arena.cursor, limit = root_frame_arena.limit;
    uintptr_t begin = root_frame_arena.begin;
    u32 depth = root_slot_count();
    fail_metadata = true;
    u32 beyond_available = (u32)((limit - cursor) / sizeof(netwasm_reference_t)) + 1;
    assert(root_frame_enter(beyond_available) == 0);
    assert(root_frame_arena.current == current && root_frame_arena.cursor == cursor &&
        root_frame_arena.limit == limit && root_frame_arena.begin == begin && root_slot_count() == depth);
    fail_metadata = false;
    for (size_t i = 96; i-- != 0;) {
        root_frame_leave(frames[i]);
        sum -= (i * 17 + 1 + i * 17 + 17) * 17 / 2;
        verify_roots(i * 17, sum);
    }
    assert(root_slot_count() == 0 && root_frame_arena.cursor == (uintptr_t)frame_arena_bytes(root_frame_arena.current));
    size_t allocations_before = metadata_allocations;
    for (size_t i = 0; i < 96; i++) {
        assert(root_frame_enter(17) == frames[i]);
        assert(frames[i] >= root_frame_arena.begin && frames[i] < root_frame_arena.cursor);
    }
    assert(metadata_allocations == allocations_before);
    for (size_t i = 96; i-- != 0;) root_frame_leave(frames[i]);
    netwasm_address_t empty = root_frame_enter(0);
    assert(empty != 0 && root_slot_count() == 0);
    verify_roots(0, 0);
    root_frame_leave(empty);
    assert(root_frame_enter(UINT32_MAX) == 0 && root_slot_count() == 0);
    release_arena(&root_frame_arena);
}

static void root_chunk_transitions(void)
{
    /* Both collecting and Bump runtimes own distinct storage for nested frames. */
    netwasm_address_t outer = root_frame_enter(65);
    assert(outer != 0);
    netwasm_reference_t *slots = (void *)(uintptr_t)outer;
    for (size_t i = 0; i < 65; i++) slots[i] = i + 1;
    FrameArenaChunk *first = root_frame_arena.current;
    assert(first->capacity == ROOT_ARENA_INITIAL_CAPACITY);
    uintptr_t first_cursor = root_frame_arena.cursor;

    u32 inner_slots = (u32)(first->capacity / sizeof(netwasm_reference_t)) + 1;
    netwasm_address_t inner = root_frame_enter(inner_slots);
    assert(inner != 0);
    memset((void *)(uintptr_t)inner, 0, inner_slots * sizeof(netwasm_reference_t));
    FrameArenaChunk *cached = root_frame_arena.current;
    assert(cached != first && cached->previous == first && first->saved_cursor == first_cursor);
    u32 depth = root_slot_count();
    uintptr_t cursor = root_frame_arena.cursor, begin = root_frame_arena.begin;
    uintptr_t limit = root_frame_arena.limit, saved_cursor = cached->saved_cursor;
    fail_metadata = true;
    assert(root_frame_enter(65535) == 0);
    assert(root_frame_arena.current == cached && root_frame_arena.cursor == cursor &&
        root_frame_arena.begin == begin && root_frame_arena.limit == limit && root_slot_count() == depth &&
        cached->saved_cursor == saved_cursor && cached->previous == first);
    fail_metadata = false;
    root_frame_leave(inner);
    assert(root_frame_arena.current == first && root_frame_arena.cursor == first_cursor &&
        root_frame_arena.begin == (uintptr_t)frame_arena_bytes(first));
    verify_roots(65, 65 * 66 / 2);

    /* Insert a larger chunk, then reuse and reparent its cached successor. */
    u32 large_slots = inner_slots + 1024;
    netwasm_address_t large = root_frame_enter(large_slots);
    assert(large != 0);
    memset((void *)(uintptr_t)large, 0, large_slots * sizeof(netwasm_reference_t));
    FrameArenaChunk *inserted = root_frame_arena.current;
    assert(inserted != cached && inserted->next == cached && inserted->previous == first);
    uintptr_t large_cursor = root_frame_arena.cursor;
    size_t allocations_before = metadata_allocations;
    netwasm_address_t reused = root_frame_enter(65);
    assert(reused != 0 && root_frame_arena.current == cached && cached->previous == inserted &&
        metadata_allocations == allocations_before);
    memset((void *)(uintptr_t)reused, 0, 65 * sizeof(netwasm_reference_t));
    verify_roots(65, 65 * 66 / 2);
    root_frame_leave(reused);
    assert(root_frame_arena.current == inserted && root_frame_arena.cursor == large_cursor);
    root_frame_leave(large);
    assert(root_frame_arena.current == first && root_frame_arena.cursor == first_cursor);
    verify_roots(65, 65 * 66 / 2);
    root_frame_leave(outer);
    assert(root_slot_count() == 0 && root_frame_arena.cursor == root_frame_arena.begin);
    release_arena(&root_frame_arena);
}

static void root_frame_capacity_boundary(void)
{
    /* Use more than the Bump scratch capacity to exercise the shared arena. */
    netwasm_address_t outer = root_frame_enter(65);
    assert(outer != 0);
    memset((void *)(uintptr_t)outer, 0, 65 * sizeof(netwasm_reference_t));
    ((netwasm_reference_t *)(uintptr_t)outer)[0] = 17;
    uintptr_t cursor = root_frame_arena.cursor;
    FrameArenaChunk *chunk = root_frame_arena.current;
    size_t available = root_frame_arena.limit - cursor;
    u32 slots = (u32)(available / sizeof(netwasm_reference_t));
    assert(slots > 64);
    size_t before = metadata_allocations;
    netwasm_address_t filled = root_frame_enter(slots);
    assert(filled != 0 && root_frame_arena.cursor == root_frame_arena.limit &&
        root_frame_arena.current == chunk && metadata_allocations == before);
    memset((void *)(uintptr_t)filled, 0, slots * sizeof(netwasm_reference_t));
    ((netwasm_reference_t *)(uintptr_t)filled)[slots - 1] = 23;
    verify_roots(2, 40);

    u32 depth = root_slot_count();
    netwasm_address_t empty = root_frame_enter(0);
    assert(empty != 0 && root_slot_count() == depth);
    fail_metadata = true;
    assert(root_frame_enter(65) == 0 && root_slot_count() == depth &&
        root_frame_arena.current == chunk && root_frame_arena.cursor == root_frame_arena.limit);
    fail_metadata = false;
    netwasm_address_t next = root_frame_enter(65);
    assert(next != 0 && root_frame_arena.current != chunk);
    memset((void *)(uintptr_t)next, 0, 65 * sizeof(netwasm_reference_t));
    verify_roots(2, 40);
    root_frame_leave(next);
    assert(root_frame_arena.current == chunk && root_frame_arena.cursor == root_frame_arena.limit);
    root_frame_leave(empty);
    root_frame_leave(filled);
    assert(root_frame_arena.cursor == cursor);
    verify_roots(1, 17);
    root_frame_leave(outer);
    assert(root_slot_count() == 0);
    release_arena(&root_frame_arena);
}

static void value_frames(void)
{
    fail_metadata = true;
    assert(value_frame_enter(1) == 0 && value_frame == NULL && value_frame_arena.current == NULL);
    fail_metadata = false;
    netwasm_address_t outer = value_frame_enter(32);
    /* A frame without references stays off the list a collection traces. */
    assert(outer != 0 && outer % 16 == 0 && value_frame == NULL);
    memset((void *)(uintptr_t)outer, 0x5a, 32);
    FrameArenaChunk *first = value_frame_arena.current;
    netwasm_address_t middle = value_frame_enter(8192);
    assert(middle != 0 && middle % 16 == 0 && value_frame_arena.current != first);
    FrameArenaChunk *second = value_frame_arena.current;
    memset((void *)(uintptr_t)middle, 0xa5, 8192);
    assert(value_frame_enter(8192) != 0 && value_frame_arena.current != second);
    value_frame_leave(middle); /* An exception may unwind several value frames. */
    assert(value_frame == NULL && value_frame_arena.current == second &&
        value_frame_arena.cursor == (uintptr_t)middle - sizeof(ValueFrame));
    size_t allocations_before = metadata_allocations;
    assert(value_frame_enter(8192) == middle && metadata_allocations == allocations_before);
    for (size_t i = 0; i < 32; i++) assert(((unsigned char *)(uintptr_t)outer)[i] == 0x5a);
    value_frame_leave(middle);
    value_frame_leave(outer);
    assert(value_frame == NULL && value_frame_arena.current == first &&
        value_frame_arena.cursor == (uintptr_t)frame_arena_bytes(first));
    assert(value_frame_enter(UINT32_MAX) == 0 && value_frame == NULL);
    release_arena(&value_frame_arena);
}

/* A value frame's map names the words a collection traces, as they are when
   it runs: a reference stored into the frame after it was entered is seen, the
   scalars beside it are not, and a frame without a map adds nothing. A map of
   one word stays in the frame's header and a longer one follows its bytes.
   Leaving a mapped frame, whose chunk carries its tags, rewinds the arena as
   leaving any frame does, within its chunk and from a chunk of its own, and the
   frames entered after it stay aligned. */
static void mapped_value_frames(void)
{
    netwasm_address_t plain = value_frame_enter(32);
    /* A frame without a map is not on the list. */
    assert(plain != 0 && value_frame == NULL);
    memset((void *)(uintptr_t)plain, 0x5a, 32);
    verify_roots(0, 0);
    FrameArenaChunk *first = value_frame_arena.current;
    uintptr_t cursor = value_frame_arena.cursor;
    const size_t words = 80;
    netwasm_address_t mapped = value_frame_enter_mapped(
        (netwasm_address_t)(words * sizeof(netwasm_reference_t)), 2, ((u64)1 << 0) | ((u64)1 << 3));
    assert(mapped != 0 && mapped % 16 == 0 && value_frame->references >> 32 == 2);
    assert((uintptr_t)value_frame->chunk == ((uintptr_t)first | 3u) && value_frame_arena.current == first);
    netwasm_reference_t *storage = (void *)(uintptr_t)mapped;
    /* The caller stores the map's later words after the rounded bytes. */
    size_t rounded = (words * sizeof(netwasm_reference_t) + 15) & ~(size_t)15;
    u64 *map = (u64 *)(void *)((unsigned char *)storage + rounded);
    assert((u32)value_frame->references == rounded &&
        map[0] == (((u64)1 << 0) | ((u64)1 << 3)) && map[1] == 0);
    map[1] = (u64)1 << 6;
    for (size_t i = 0; i < words; i++) storage[i] = 0x1000 + i;
    verify_roots(3, 0x1000 + 0x1003 + 0x1046);
    storage[3] = 0x9000;
    verify_roots(3, 0x1000 + 0x9000 + 0x1046);
    storage[0] = 0;
    verify_roots(2, 0x9000 + 0x1046);
    /* Leaving the mapped frame also leaves a frame above it in a later chunk. */
    assert(value_frame_enter(8192) != 0 && value_frame_arena.current != first);
    value_frame_leave_mapped(mapped);
    verify_roots(0, 0);
    assert(value_frame_arena.current == first && value_frame_arena.cursor == cursor);
    /* A map of one word takes no room after the bytes, and is traced. */
    netwasm_address_t single = value_frame_enter_mapped(16, 1, 2);
    assert(single != 0 && (uintptr_t)value_frame->chunk == ((uintptr_t)first | 1u));
    ((netwasm_reference_t *)(uintptr_t)single)[0] = 0x2000;
    ((netwasm_reference_t *)(uintptr_t)single)[1] = 0x3000;
    verify_roots(1, 0x3000);
    netwasm_address_t next = value_frame_enter(16);
    assert(next == single + 16 + sizeof(ValueFrame) && value_frame_arena.current == first);
    value_frame_leave_mapped(single);
    assert(value_frame_arena.cursor == cursor);
    netwasm_address_t spilled = value_frame_enter_mapped(8192, 1, 1);
    FrameArenaChunk *own = value_frame_arena.current;
    assert(spilled != 0 && own != first && (uintptr_t)value_frame->chunk == ((uintptr_t)own | 1u));
    value_frame_leave_mapped(spilled);
    assert(value_frame_arena.current == own && value_frame_arena.cursor == (uintptr_t)frame_arena_bytes(own));
    value_frame_leave(plain);
    assert(value_frame == NULL && value_frame_arena.current == first &&
        value_frame_arena.cursor == (uintptr_t)frame_arena_bytes(first));
    assert(value_frame_enter_mapped(16, UINT32_MAX, 1) == 0 && value_frame == NULL);
    release_arena(&value_frame_arena);
}

/* Leaving a frame without references restores the list as it stood when the
   frame was entered, so it also leaves the mapped frames an exception left
   above it and a collection no longer traces them. */
static void plain_frames_leave_mapped_frames_above(void)
{
    netwasm_address_t plain = value_frame_enter(32);
    netwasm_address_t mapped = value_frame_enter_mapped(16, 1, 1);
    assert(plain != 0 && mapped != 0 && value_frame->bytes == (unsigned char *)(uintptr_t)mapped);
    ((netwasm_reference_t *)(uintptr_t)mapped)[0] = 0x4000;
    netwasm_address_t inner = value_frame_enter(16);
    assert(inner != 0 && value_frame->bytes == (unsigned char *)(uintptr_t)mapped);
    verify_roots(1, 0x4000);
    value_frame_leave(plain);
    assert(value_frame == NULL && value_frame_arena.cursor == (uintptr_t)plain - sizeof(ValueFrame));
    verify_roots(0, 0);
    release_arena(&value_frame_arena);
}

/* Methods leave their frames only on normal exit. An exception frame records
   where the arenas stood when its region began, and its landing pad rewinds to
   that point however many frames and chunks the exception left behind. */
static void exception_frame_rewinds(void)
{
    netwasm_address_t outer = root_frame_enter(65);
    assert(outer != 0);
    netwasm_reference_t *slots = (void *)(uintptr_t)outer;
    for (size_t i = 0; i < 65; i++) slots[i] = i + 1;
    netwasm_address_t outer_value = value_frame_enter(32);
    assert(outer_value != 0);
    memset((void *)(uintptr_t)outer_value, 0x5a, 32);
    stack_trace_frame_enter(7);
    FrameArenaChunk *first = root_frame_arena.current;
    FrameArenaChunk *first_value = value_frame_arena.current;
    uintptr_t value_mark = value_frame_arena.cursor;
    uintptr_t root_mark = root_frame_arena.cursor;
    u32 token = exception_frame_enter(0, 0);
    assert(token == exception_frame_top);

    /* The exception passes through frames that cross into new chunks. */
    u32 large_slots = (u32)(first->capacity / sizeof(netwasm_reference_t)) + 1;
    netwasm_address_t large = root_frame_enter(large_slots);
    assert(large != 0 && root_frame_arena.current != first);
    for (u32 i = 0; i < large_slots; i++) ((netwasm_reference_t *)(uintptr_t)large)[i] = 1000;
    for (size_t i = 0; i < 8; i++) {
        netwasm_address_t frame = root_frame_enter(600);
        assert(frame != 0);
        for (size_t j = 0; j < 600; j++) ((netwasm_reference_t *)(uintptr_t)frame)[j] = 1000;
    }
    netwasm_address_t middle_value = value_frame_enter(8192);
    assert(middle_value != 0 && value_frame_arena.current != first_value);
    netwasm_address_t mapped_value = value_frame_enter_mapped(16, 1, 1);
    assert(mapped_value != 0);
    ((netwasm_reference_t *)(uintptr_t)mapped_value)[0] = 0x5000;
    for (size_t i = 0; i < 4; i++) assert(value_frame_enter(8192) != 0);
    stack_trace_frame_enter(8);
    stack_trace_frame_enter(9);

    exception_frame_unwind(token);
    assert(exception_frame_top == token - 1);
    assert(root_frame_arena.current == first && root_frame_arena.cursor == root_mark &&
        root_frame_arena.begin == (uintptr_t)frame_arena_bytes(first) &&
        root_frame_arena.limit == root_frame_arena.begin + first->capacity);
    verify_roots(65, 65 * 66 / 2);
    assert(value_frame == NULL && value_frame_arena.current == first_value &&
        value_frame_arena.cursor == value_mark);
    assert(stack_trace_frame_top == 1 && stack_trace_frames[0].method_id == 7);

    /* The arenas carry on as if the skipped frames had left normally: their
       cached chunks are reused and the outer frames leave in order. */
    size_t allocations_before = metadata_allocations;
    assert(value_frame_enter(8192) == middle_value);
    netwasm_address_t again = root_frame_enter(large_slots);
    assert(again == large && metadata_allocations == allocations_before);
    memset((void *)(uintptr_t)again, 0, large_slots * sizeof(netwasm_reference_t));
    root_frame_leave(again);
    value_frame_leave(middle_value);
    stack_trace_frame_leave(7);
    value_frame_leave(outer_value);
    root_frame_leave(outer);
    assert(root_slot_count() == 0 && value_frame == NULL && stack_trace_frame_top == 0);

    /* A region that an exception leaves with only frames without references
       above it still rewinds the value arena to where it began. */
    netwasm_address_t base = value_frame_enter(32);
    uintptr_t base_mark = value_frame_arena.cursor;
    token = exception_frame_enter(0, 0);
    assert(value_frame_enter(64) != 0 && value_frame_enter(8192) != 0);
    exception_frame_unwind(token);
    assert(value_frame == NULL && value_frame_arena.cursor == base_mark);
    value_frame_leave(base);
    assert(value_frame_arena.cursor == (uintptr_t)base - sizeof(ValueFrame));

    /* A normal exit leaves the exception frame without touching the arenas. */
    netwasm_address_t kept = root_frame_enter(3);
    token = exception_frame_enter(0, 0);
    uintptr_t cursor = root_frame_arena.cursor;
    exception_frame_leave(token);
    assert(root_frame_arena.cursor == cursor && root_slot_count() == 3);
    root_frame_leave(kept);
    release_arena(&root_frame_arena);
    release_arena(&value_frame_arena);

    /* A region entered before the first frame rewinds to the empty arenas. */
    token = exception_frame_enter(0, 0);
    netwasm_address_t first_frame = root_frame_enter(5);
    assert(first_frame != 0 && root_frame_enter(large_slots) != 0);
    assert(value_frame_enter(64) != 0);
    exception_frame_unwind(token);
    assert(root_slot_count() == 0 && root_frame_arena.current->previous == NULL &&
        root_frame_arena.cursor == root_frame_arena.begin && value_frame == NULL);
    verify_roots(0, 0);
    release_arena(&root_frame_arena);
    release_arena(&value_frame_arena);
    collector_release_metadata(exception_frames);
    exception_frames = NULL;
    exception_frame_capacity = 0;
    collector_release_metadata(stack_trace_frames);
    stack_trace_frames = NULL;
    stack_trace_frame_capacity = 0;
}

int main(void)
{
    root_frames();
    netwasm_address_t empty = root_frame_enter(0);
    assert(empty != 0 && root_slot_count() == 0);
    verify_roots(0, 0);
    root_frame_leave(empty);
    release_arena(&root_frame_arena);
    root_chunk_transitions();
    root_frame_capacity_boundary();
    value_frames();
    mapped_value_frames();
    plain_frames_leave_mapped_frames_above();
    exception_frame_rewinds();
    assert(metadata_allocations == metadata_releases);
    return 0;
}
