using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

public sealed class UnitFingerprintTests
{
    [Fact]
    public void EqualContentFingerprintsEquallyAndAnyFieldChangesIt()
    {
        var layout = new ObjectLayout(7, 24, [8, 16]) { LastSubtypeId = 9 };

        Assert.Equal(Of(layout), Of(layout with { ReferenceOffsets = [8, 16] }));
        Assert.NotEqual(Of(layout), Of(layout with { TypeId = 8 }));
        Assert.NotEqual(Of(layout), Of(layout with { LastSubtypeId = 10 }));
        Assert.NotEqual(Of(layout), Of(layout with { Size = 32 }));
        Assert.NotEqual(Of(layout), Of(layout with { ReferenceOffsets = [8] }));
    }

    [Fact]
    public void LengthPrefixesKeepAdjacentValuesApart()
    {
        var joined = new UnitFingerprintBuilder();
        joined.Add("ab");
        joined.Add("c");
        var split = new UnitFingerprintBuilder();
        split.Add("a");
        split.Add("bc");
        var absent = new UnitFingerprintBuilder();
        absent.Add((string?)null);
        var empty = new UnitFingerprintBuilder();
        empty.Add("");

        Assert.NotEqual(joined.Finish(), split.Finish());
        Assert.NotEqual(absent.Finish(), empty.Finish());
        Assert.Equal(new UnitFingerprintBuilder().Finish(), new UnitFingerprintBuilder().Finish());
    }

    [Fact]
    public void AMethodFingerprintsByItsInstructionsAndRootMap()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EmitterTestSupport.EntryKey);
        StructuredMethod Returning(int value) => EmitterTestSupport.Structure(program, method,
            EmitterTestSupport.I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(value)),
            EmitterTestSupport.I(1, CilOperation.Return));
        var map = new MethodRootMap(method.Key, ImmutableDictionary<RootSource, int>.Empty,
            ImmutableDictionary<int, SafepointRootMap>.Empty);
        var rooted = map with
        {
            Slots = ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Local, 0), 0),
        };

        Assert.Equal(Of(Returning(42), map), Of(Returning(42), map));
        Assert.NotEqual(Of(Returning(42), map), Of(Returning(43), map));
        Assert.NotEqual(Of(Returning(42), map), Of(Returning(42), rooted));
    }

    private static UnitFingerprint Of(StructuredMethod method, MethodRootMap map)
    {
        var builder = new UnitFingerprintBuilder();
        builder.Add(method);
        builder.Add(map);
        return builder.Finish();
    }

    private static UnitFingerprint Of(ObjectLayout layout)
    {
        var builder = new UnitFingerprintBuilder();
        builder.Add(layout);
        return builder.Finish();
    }
}
