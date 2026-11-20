// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

// A by-reference local that holds a variable's address is that address first, so the variable's accesses through it
// are accesses through the variable's own address; a vector variable's lane stores through its address then become
// stores of its value, which leaves its lane reads and whole-value accesses through an address for the dereference
// folder, so a variable whose every address goes keeps no value-frame storage.
internal sealed class LaneStoringLocalAddressDereferenceFolder(
    IByReferenceLocalForwarder references,
    IVectorLocalLaneStoreRewriter laneStores,
    ILocalAddressDereferenceFolder dereferences) : ILocalAddressDereferenceFolder
{
    private readonly IByReferenceLocalForwarder _references = references ?? throw new ArgumentNullException(nameof(references));
    private readonly IVectorLocalLaneStoreRewriter _laneStores = laneStores ?? throw new ArgumentNullException(nameof(laneStores));
    private readonly ILocalAddressDereferenceFolder _dereferences = dereferences ??
        throw new ArgumentNullException(nameof(dereferences));

    public CilMethodBody Rewrite(CilMethodBody body) => _dereferences.Rewrite(_laneStores.Rewrite(_references.Rewrite(body)));
}
