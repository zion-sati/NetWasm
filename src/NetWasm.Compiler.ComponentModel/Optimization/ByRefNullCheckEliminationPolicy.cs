// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

// Whether a read or write through a managed address the compiler proves non-null skips its null check.
public enum ByRefNullCheckEliminationPolicy
{
    Disabled,
    // The address of a local, argument, static, array element or field, and an address already dereferenced.
    ProvenAddresses,
    // ProvenAddresses, and a loop's reads of a reference plus its counter in a copy of the loop that runs where a test
    // ahead of it finds none of those addresses is 0 (the original loop runs elsewhere): speed for the copy's size.
    VersionedLoops,
}
