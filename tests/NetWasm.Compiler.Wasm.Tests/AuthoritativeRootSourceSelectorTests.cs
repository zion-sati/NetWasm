using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AuthoritativeRootSourceSelectorTests
{
    [Fact]
    public void ExistingAddressableAndCachedStorageIsNotRedirected()
    {
        var selected = new RootSource(RootSourceKind.Argument, 0);
        var local = new RootSource(RootSourceKind.Local, 0);
        var spilled = new RootSource(RootSourceKind.Local, 1);
        var argument = new RootSource(RootSourceKind.Argument, 1);
        var cached = new RootSource(RootSourceKind.Local, 2);
        var captured = new RootSource(RootSourceKind.Local, 3);
        var indirect = new RootSource(RootSourceKind.Local, 4, 0);
        var temporary = new RootSource(RootSourceKind.EvaluationStack, 0);
        var roots = new MethodRootMap(EntryKey,
            new[] { selected, local, spilled, argument, cached, captured, indirect, temporary }
                .Select((source, slot) => (source, slot)).ToImmutableDictionary(x => x.source, x => x.slot), []);
        var values = new ValueFrameLayout(16, [], ImmutableDictionary<int, int>.Empty.Add(1, 0), [], [1]);
        var captureSlot = new CapturedSlot(false, 3);
        var filters = new FilterEnvironmentLayout(16, 1, 0,
            ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(captureSlot,
                new(captureSlot, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), 0, [])));
        var selector = new[] { new AuthoritativeRootSourceSelector() }
            .Cast<IAuthoritativeRootSourceSelector>().Single();
        var result = selector.Select(roots, values, filters, new ArrayMetadataCachingPlan([new(false, 2)], []));
        Assert.Equal(2, result.Count);
        Assert.Contains(selected, result);
        Assert.Contains(local, result);
    }

    // A byref argument keeps one value for the call unless stored, so its slot is written at entry
    // and on a store instead of at every safepoint; one held in the value frame or captured by a
    // filter is not.
    [Fact]
    public void ByrefArgumentsAreAuthoritativeUnlessFramedOrCaptured()
    {
        var receiver = new RootSource(RootSourceKind.ManagedAddressArgument, 0);
        var framed = new RootSource(RootSourceKind.ManagedAddressArgument, 1);
        var captured = new RootSource(RootSourceKind.ManagedAddressArgument, 2);
        var byrefLocal = new RootSource(RootSourceKind.ManagedAddressLocal, 0);
        var roots = new MethodRootMap(EntryKey,
            new[] { receiver, framed, captured, byrefLocal }
                .Select((source, slot) => (source, slot)).ToImmutableDictionary(x => x.source, x => x.slot), []);
        var values = new ValueFrameLayout(16, [], ImmutableDictionary<int, int>.Empty.Add(1, 0), [], []);
        var captureSlot = new CapturedSlot(true, 2);
        var filters = new FilterEnvironmentLayout(16, 1, 0,
            ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(captureSlot,
                new(captureSlot, CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress), 0, [])));

        var result = new AuthoritativeRootSourceSelector().Select(roots, values, filters, new ArrayMetadataCachingPlan([], []));

        Assert.Equal([receiver], result);
    }
}
