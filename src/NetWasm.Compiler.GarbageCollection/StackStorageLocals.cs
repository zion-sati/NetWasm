// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.GarbageCollection;

// The storage a method's locals address at an instruction, by local index. Only a byref local addresses storage; every
// other local reads as addressing none. So the analysis keeps the byref locals alone, the index of each byref local's
// slot is shared by every instruction of the method, and an instruction whose locals did not change since the one
// before shares its storage too: a method's other locals cost nothing per instruction.
public sealed class StackStorageLocals
{
    private static readonly StackStorageType None = new(null, false);

    private readonly ImmutableArray<int> _slots;
    private readonly ImmutableArray<StackStorageType> _addressed;

    // Every local, as its own slot.
    public StackStorageLocals(ImmutableArray<StackStorageType> locals)
        : this([.. Enumerable.Range(0, locals.Length)], locals)
    {
    }

    // slots gives each local's index in addressed, or -1 for a local that addresses no storage.
    internal StackStorageLocals(ImmutableArray<int> slots, ImmutableArray<StackStorageType> addressed)
    {
        _slots = slots;
        _addressed = addressed;
    }

    public int Length => _slots.Length;

    public StackStorageType this[int local] => _slots[local] is var slot and >= 0 ? _addressed[slot] : None;
}
