using System.Collections.Immutable;
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

    // An instruction fingerprints by its operand's kind and value; every kind has a form, so
    // no two instructions differing only in operand fingerprint alike.
    [Fact]
    public void EveryOperandKindFingerprintsApart()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EmitterTestSupport.EntryKey);
        var type = CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Test", "Type", isValueType: false);
        var instance = new MethodInstanceModel(method, type, [], method.Signature);
        var definition = new FieldDefinitionModel(new(EmitterTestSupport.Assembly, 0x04000001), method.Key, "Data", type, true)
        {
            InitialData = [1, 2, 3, 4],
        };
        var field = new FieldInstanceModel(definition, type, type);
        CilOperand[] operands =
        [
            new CilOperand.None(), new CilOperand.ConstantI4(1), new CilOperand.ConstantI8(1), new CilOperand.ConstantF4(1),
            new CilOperand.ConstantF8(1), new CilOperand.Index(1), new CilOperand.BranchTarget(1),
            new CilOperand.SwitchTargets([1, 2]), new CilOperand.Entity(method.Key), new CilOperand.MethodInstance(instance),
            new CilOperand.FieldInstance(field), new CilOperand.TypeIdentity(type), new CilOperand.ExactType(type),
            new CilOperand.CallSite(method.Signature), new CilOperand.NumericConversion(32, false, true, false, false),
            new CilOperand.UserString("text"), new CilOperand.ArrayInitializerData(field),
            new CilOperand.ReadOnlySpanData(field, type, type, 4),
        ];
        var kinds = typeof(CilOperand).GetNestedTypes().Where(nested => nested.IsSubclassOf(typeof(CilOperand)));

        Assert.Equal(kinds.Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name))),
            operands.Select(operand => operand.GetType()).Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name))));
        var fingerprints = operands.Select(operand => Of(new CilInstruction(0, 1, CilOperation.Nop, operand))).ToArray();
        Assert.Equal(fingerprints.Length, fingerprints.Distinct().Count());
        Assert.Equal(fingerprints[3], Of(new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.ConstantF4(1))));
        // An unmanaged calli lowers to a different call than a managed one (issue 226).
        Assert.NotEqual(fingerprints[13], Of(new CilInstruction(0, 1, CilOperation.Nop,
            new CilOperand.CallSite(method.Signature, Unmanaged: true))));
    }

    [Fact]
    public void ARootMapFingerprintsByItsSlotsAndAnchors()
    {
        var method = new EntityKey(EmitterTestSupport.Assembly, 0x06000001);
        var map = new MethodRootMap(method, ImmutableDictionary<RootSource, int>.Empty,
            ImmutableDictionary<int, SafepointRootMap>.Empty);
        var rooted = map with
        {
            Slots = ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Local, 0), 0),
        };
        var anchored = map with
        {
            AnchorPlan = new([new(new(StackStorageLocationKind.Local, 0), CliTypeIdentity.Primitive("i4", CliValueKind.I4))],
                ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty.Add(4,
                    [new StackStorageAnchorAssignment.Capture(0, 1)])),
        };

        Assert.Equal(Of(map), Of(map with { }));
        Assert.NotEqual(Of(map), Of(rooted));
        Assert.NotEqual(Of(map), Of(anchored));
    }

    [Fact]
    public void ADefaultListFingerprintsApartFromAnEmptyOne()
    {
        var absent = new UnitFingerprintBuilder();
        absent.Add(default(ImmutableArray<int>));
        var empty = new UnitFingerprintBuilder();
        empty.Add(ImmutableArray<int>.Empty);

        Assert.NotEqual(absent.Finish(), empty.Finish());
    }

    private static UnitFingerprint Of(CilInstruction instruction)
    {
        var builder = new UnitFingerprintBuilder();
        builder.Add(instruction);
        return builder.Finish();
    }

    private static UnitFingerprint Of(MethodRootMap map)
    {
        var builder = new UnitFingerprintBuilder();
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
