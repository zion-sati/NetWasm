/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GC_REFERENCE_CARDS_H
#define NETWASM_GC_REFERENCE_CARDS_H

/* The generational collector's record of where references were stored since its last collection. Memory is
 * divided into cards of 256 bytes, each with one byte that a store of a reference into the card sets.
 *
 * The bytes are kept in two levels, so that nothing is paid for memory the collector does not hold. A directory
 * has one entry per span of addresses, and a span the collector has objects in has a leaf holding its cards. Every
 * other span's entry is zero, which names one shared leaf, the sink: a store through a pointer to the stack, to
 * static storage or to memory the collector does not own sets a byte there that nothing reads. An entry is the
 * leaf's distance from the sink, so a directory of zeroes needs no initializing and its untouched pages are never
 * written.
 *
 * What each unit answers for, with 64-byte cache lines on wasm32: a directory entry is 1 MiB of addresses, so a
 * line of 16 entries is 16 MiB and a program's whole heap is usually within one or two lines; a card is 256 bytes,
 * four lines of objects, so a line of 64 cards is 16 KiB and a leaf of 4 KiB is one span. A store therefore reads
 * a directory line that is always in the cache and writes one card line for every 256 lines of objects written.
 * The card's size sets what a store costs the collector: every old object on a set card is traced again.
 *
 * Where an address is wider than the directory covers, spans a whole directory apart share an entry and its leaf.
 * A store then sets a card that the collector also reads for the other span: it traces an object it did not need
 * to, and never misses one. */
#include <stddef.h>
#include <stdint.h>

#define NWGC_CARD_SHIFT 8u
#define NWGC_CARD_BYTES ((uintptr_t)1u << NWGC_CARD_SHIFT)
#if UINTPTR_MAX == UINT32_MAX
/* A span is 1 MiB and its leaf 4 KiB; the directory's 4,096 entries cover every address. */
#define NWGC_CARD_SPAN_SHIFT 20u
#define NWGC_CARD_DIRECTORY_ENTRIES ((size_t)1u << (32u - NWGC_CARD_SPAN_SHIFT))
#else
/* A span is 4 MiB and its leaf 16 KiB; the directory's 16,384 entries cover 64 GiB before spans share. */
#define NWGC_CARD_SPAN_SHIFT 22u
#define NWGC_CARD_DIRECTORY_ENTRIES ((size_t)1u << 14u)
#endif
#define NWGC_CARD_SPAN_BYTES ((uintptr_t)1u << NWGC_CARD_SPAN_SHIFT)
#define NWGC_CARD_LEAF_CARDS ((size_t)1u << (NWGC_CARD_SPAN_SHIFT - NWGC_CARD_SHIFT))

/* Every leaf's cards start on a cache line, so that one line of 64 cards answers for one aligned 16 KiB of
 * memory and the eight cards the collector reads at a time never straddle two lines. The directory and the sink
 * are static data, which the runtime aligns to no more than 16 bytes so that its size does not depend on where
 * it is placed. */
#define NWGC_CARD_LINE_BYTES 64u

extern uintptr_t netwasm_gc_card_directory[NWGC_CARD_DIRECTORY_ENTRIES];
extern unsigned char netwasm_gc_card_sink[NWGC_CARD_LEAF_CARDS];

/* The byte of the card that holds the address. */
static inline __attribute__((always_inline)) unsigned char *netwasm_gc_card(uintptr_t address)
{
    uintptr_t leaf = (uintptr_t)netwasm_gc_card_sink +
        netwasm_gc_card_directory[(size_t)(address >> NWGC_CARD_SPAN_SHIFT) & (NWGC_CARD_DIRECTORY_ENTRIES - 1)];
    return (unsigned char *)(leaf + ((address >> NWGC_CARD_SHIFT) & (NWGC_CARD_LEAF_CARDS - 1)));
}

/* The write barrier: a reference was stored at the address, or into the object or value that starts there. One
 * load and one store, with no test, so that the compiler can place it after every store of a reference. */
static inline __attribute__((always_inline)) void netwasm_gc_record_reference_write(uintptr_t address)
{
    *netwasm_gc_card(address) = 1;
}

/* A range of memory received references: every card it overlaps is set. */
static inline void netwasm_gc_record_reference_writes(uintptr_t first, size_t bytes)
{
    if (bytes == 0) return;
    uintptr_t last = first + (bytes - 1);
    for (uintptr_t card = first & ~(NWGC_CARD_BYTES - 1);; card += NWGC_CARD_BYTES) {
        netwasm_gc_record_reference_write(card);
        if (last - card < NWGC_CARD_BYTES) break;
    }
}

#endif
