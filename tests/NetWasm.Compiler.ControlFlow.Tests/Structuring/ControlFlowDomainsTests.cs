// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// The index answers what a scan over the domains answers, for domains the finder makes (each block claimed once) and
// for any other list: overlapping domains, domains sharing an entry, and blocks no domain claims.
public sealed class ControlFlowDomainsTests
{
    [Fact]
    public void AnswersAsAScanOverTheDomainsDoes()
    {
        var random = new Random(20261122);
        for (var round = 0; round < 200; round++)
        {
            var domains = Enumerable.Range(0, random.Next(1, 6))
                .Select(_ => new ControlFlowDomain(random.Next(0, 4),
                    [.. Enumerable.Range(0, random.Next(0, 8)).Select(_ => random.Next(0, 20))]))
                .ToImmutableArray();
            var index = new ControlFlowDomains(domains);
            foreach (var domain in domains)
            {
                for (var block = 0; block < 22; block++)
                {
                    Assert.Equal(
                        domains.Any(other => other.Entry != domain.Entry && other.Blocks.Contains(block)),
                        index.ClaimsOutside(domain, block));
                    Assert.Equal(domains.Any(other => other.Blocks.Contains(block)), index.Claims(block));
                }
            }
        }
    }

    [Fact]
    public void ABlockOfTwoEntriesIsOutsideEitherAndOneEntrysBlocksAreInsideIt()
    {
        var first = new ControlFlowDomain(0, [0, 1, 5]);
        var second = new ControlFlowDomain(3, [3, 4, 5]);
        var again = new ControlFlowDomain(0, [1, 2]);
        ControlFlowDomains domains = [first, second, again];

        Assert.True(domains.ClaimsOutside(first, 5));
        Assert.True(domains.ClaimsOutside(second, 5));
        Assert.False(domains.ClaimsOutside(again, 1));
        Assert.True(domains.ClaimsOutside(first, 4));
        Assert.False(domains.ClaimsOutside(first, 9));
        Assert.False(domains.Claims(9));
        Assert.Equal([first, second, again], domains.ToArray());
        Assert.Equal(3, domains.Count);
        Assert.Same(second, domains[1]);
    }

    // Fifty thousand handlers, each a domain with a loop: each loop's entries are found from its predecessors' domains,
    // one lookup each, where a scan over every domain for each predecessor took the handlers squared.
    [Fact]
    public void ManyDomainsFindEachLoopsEntriesWithoutScanningThem()
    {
        const int handlers = 50_000;
        var domains = new List<ControlFlowDomain> { new(0, [0]) };
        var successors = new Dictionary<int, ImmutableArray<int>>();
        var predecessors = new Dictionary<int, ImmutableArray<int>> { [0] = [] };
        for (var handler = 0; handler < handlers; handler++)
        {
            // A handler's entry, its loop's header and body: the header is entered from the entry and the body.
            var entry = 1 + handler * 3;
            domains.Add(new ControlFlowDomain(entry, [entry, entry + 1, entry + 2]));
            (successors[entry], successors[entry + 1], successors[entry + 2]) = ([entry + 1], [entry + 2], [entry + 1]);
            (predecessors[entry], predecessors[entry + 1], predecessors[entry + 2]) = ([0], [entry, entry + 2], [entry + 1]);
        }
        successors[0] = [.. Enumerable.Range(0, handlers).Select(handler => 1 + handler * 3)];
        var blocks = predecessors.Count;
        var empty = Enumerable.Range(0, blocks).ToImmutableDictionary(block => block, _ => ImmutableArray<int>.Empty);
        var graph = new ControlFlowGraph(
            ControlFlowStructuringStateTestFactory.Create().Graph.MethodBody,
            [.. Enumerable.Range(0, blocks).Select(block => new BasicBlock(block, block, []))],
            successors,
            predecessors,
            empty,
            empty,
            [.. Enumerable.Range(0, blocks)]);
        var index = new ControlFlowDomains([.. domains]);
        var finder = new LoopEntryTargetFinder();
        var started = System.Diagnostics.Stopwatch.StartNew();

        for (var handler = 0; handler < handlers; handler++)
        {
            var entry = 1 + handler * 3;
            Assert.Equal([entry + 1], finder.Find(graph, [entry + 1, entry + 2], index[handler + 1], index));
        }
        Assert.InRange(started.Elapsed.TotalSeconds, 0, 10);
    }
}
