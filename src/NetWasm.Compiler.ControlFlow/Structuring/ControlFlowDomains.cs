// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A method's control-flow domains, with the entry of the domain that claims each block indexed once, so whether a block
// belongs to a domain other than a loop's, or to any domain, is one lookup however many domains the method has. Asked
// against every domain for each loop, those questions made a method with a loop in each of its handlers structure in
// time growing with its handlers squared. The domain finder claims each block once; a block that domains of two
// entries claim is claimed outside every domain, as a scan over the domains finds it.
[CollectionBuilder(typeof(ControlFlowDomains), nameof(Create))]
internal sealed class ControlFlowDomains : IReadOnlyList<ControlFlowDomain>
{
    private const int SeveralEntries = -1;

    private readonly ImmutableArray<ControlFlowDomain> _domains;
    private readonly Dictionary<int, int> _entryByBlock = [];

    public ControlFlowDomains(ImmutableArray<ControlFlowDomain> domains)
    {
        _domains = domains;
        foreach (var domain in domains)
        {
            foreach (var block in domain.Blocks)
            {
                _entryByBlock[block] = _entryByBlock.TryGetValue(block, out var entry) && entry != domain.Entry
                    ? SeveralEntries
                    : domain.Entry;
            }
        }
    }

    public int Count => _domains.Length;

    public ControlFlowDomain this[int index] => _domains[index];

    public static ControlFlowDomains Create(ReadOnlySpan<ControlFlowDomain> domains) => new([.. domains]);

    // Whether a domain whose entry is not the given domain's claims the block.
    public bool ClaimsOutside(ControlFlowDomain domain, int block)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return _entryByBlock.TryGetValue(block, out var entry) && entry != domain.Entry;
    }

    // Whether some domain claims the block.
    public bool Claims(int block) => _entryByBlock.ContainsKey(block);

    public ImmutableArray<ControlFlowDomain>.Enumerator GetEnumerator() => _domains.GetEnumerator();

    IEnumerator<ControlFlowDomain> IEnumerable<ControlFlowDomain>.GetEnumerator() =>
        ((IEnumerable<ControlFlowDomain>)_domains).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_domains).GetEnumerator();
}
