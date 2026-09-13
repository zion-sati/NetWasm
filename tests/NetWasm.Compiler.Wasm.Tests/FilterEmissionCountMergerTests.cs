using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FilterEmissionCountMergerTests
{
    [Fact]
    public void EmptyFilterCountsLeaveOwnerUnchanged()
    {
        var method = CreateMethod();
        var records = new List<ManagedMethodEmissionRecord>();

        CreateMerger().Merge(records, method, ImmutableDictionary<int, int>.Empty);

        Assert.Empty(records);
    }

    [Fact]
    public void MergesExistingAndNewBlockCountsIntoMatchingOwner()
    {
        var method = CreateMethod();
        var methodKey = ManagedMethodBodyKey.Resolve(method);
        var records = new List<ManagedMethodEmissionRecord>
        {
            new("other", "other-key", CreateEmission(
                "other-key",
                ImmutableDictionary<int, int>.Empty)),
            new("owner", methodKey, CreateEmission(
                methodKey,
                ImmutableDictionary<int, int>.Empty.Add(1, 2))),
        };

        CreateMerger().Merge(
            records,
            method,
            ImmutableDictionary<int, int>.Empty
                .Add(1, 3)
                .Add(2, 4));

        Assert.Empty(records[0].Emission.OriginalBlockEmissionCounts);
        Assert.Equal(5, records[1].Emission.OriginalBlockEmissionCounts[1]);
        Assert.Equal(4, records[1].Emission.OriginalBlockEmissionCounts[2]);
        Assert.Equal("owner", records[1].Identity);
    }

    [Fact]
    public void RejectsCountsWithoutMatchingManagedMethodOwner()
    {
        var method = CreateMethod();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateMerger().Merge(
                [],
                method,
                ImmutableDictionary<int, int>.Empty.Add(1, 1)));

        Assert.Contains("has no managed method body emission", exception.Message);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var method = CreateMethod();
        var counts = ImmutableDictionary<int, int>.Empty;
        var records = new List<ManagedMethodEmissionRecord>();

        Assert.Throws<ArgumentNullException>(() =>
            CreateMerger().Merge(null!, method, counts));
        Assert.Throws<ArgumentNullException>(() =>
            CreateMerger().Merge(records, null!, counts));
        Assert.Throws<ArgumentNullException>(() =>
            CreateMerger().Merge(records, method, null!));
    }

    private static IFilterEmissionCountMerger CreateMerger() =>
        new[] { new FilterEmissionCountMerger() }
            .Cast<IFilterEmissionCountMerger>()
            .Single();

    private static StructuredMethod CreateMethod()
    {
        var program = new FakeProgram();
        return EmitterTestSupport.Structure(
            program,
            program.GetMethod(EmitterTestSupport.EntryKey),
            EmitterTestSupport.I(
                0,
                CilOperation.LoadInt32,
                new CilOperand.ConstantI4(0)),
            EmitterTestSupport.I(1, CilOperation.Return));
    }

    private static ManagedMethodBodyEmission CreateEmission(
        string methodKey,
        ImmutableDictionary<int, int> counts) => new(
        [],
        0,
        0,
        0,
        methodKey,
        FilterEnvironmentLayout.Empty,
        counts);
}
