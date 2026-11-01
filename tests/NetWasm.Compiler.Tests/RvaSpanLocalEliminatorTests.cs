using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A span local assigned once from RVA data has its length read as a constant.
// One whose assignment a protected region, handler or filter covers keeps it, as
// does one read at a branch target, where another path brings its address, or
// in code its assignment does not dominate.
public sealed class RvaSpanLocalEliminatorTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Span = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "System", "ReadOnlySpan`1", isValueType: true), [Int32]);

    // The four-element RVA span and the span's length field.
    internal static readonly CilOperand.ReadOnlySpanData Data = new(
        new FieldInstanceModel(new(new(Assembly, 0x04000001), new(Assembly, 0x02000002), "Data", Int32, true),
            CliTypeIdentity.Named(Assembly, "Fixture", "Data", isValueType: false), Int32),
        Span, Int32, 4);

    internal static readonly FieldInstanceModel Length = new(
        new(new(Assembly, 0x04000002), new(Assembly, 0x02000003), "_length", Int32, false), Span, Int32);

    public static TheoryData<string, bool> Shapes() => new()
    {
        { "length read", true },
        { "length read at a branch target", false },
        { "length read in unreachable code", false },
        { "assigned in a protected region", false },
        { "assigned in a handler", false },
        { "assigned in a filter", false },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ReadsTheLengthOfASpanAssignedOnceAheadOfItsReads(string shape, bool rewritten)
    {
        var body = Shape(shape);

        var result = new RvaSpanLocalEliminator(new ControlFlowGraphBuilderFactory().Create(), new ControlFlowDominanceAnalyzer(),
            enabled: true).Rewrite(body);

        Assert.Equal(rewritten, !ReferenceEquals(body, result));
        if (rewritten)
        {
            Assert.Contains(result.Instructions, static instruction => instruction is
            {
                Operation: CilOperation.LoadInt32,
                Operand: CilOperand.ConstantI4 { Value: 4 },
            });
        }
    }

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "length read" => Body(code => code.Assign().ReadLength().Return()),
        // Dead code after the return jumps to the field load.
        "length read at a branch target" => Body(code => code.Assign().Local(CilOperation.LoadLocalAddress, 0).Label("load")
            .Op(CilOperation.LoadField, new CilOperand.FieldInstance(Length)).Op(CilOperation.Pop).Return()
            .Branch(CilOperation.Branch, "load")),
        "length read in unreachable code" => Body(code => code.Assign().Return().ReadLength().Return()),
        "assigned in a protected region" => Body(code => code.Assign().ReadLength().Return()) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 1, null, null)],
        },
        "assigned in a handler" => Body(code => code.Assign().ReadLength().Return()) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 2, 1, 0, 2, null, null)],
        },
        _ => Body(code => code.Assign().ReadLength().Return()) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Filter, 4, 1, 2, 1, null, 0)],
        },
    };

    // static void Run(), with one ReadOnlySpan<int> local.
    private static CilMethodBody Body(Func<CilBuilder, CilBuilder> write) =>
        new(new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1), 2, [CliValueKind.ValueType], write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [Span],
        };
}

// The span local writes the eliminator tests compose.
internal static class RvaSpanCil
{
    extension(CilBuilder code)
    {
        // span = RVA data.
        public CilBuilder Assign() => code.Op(CilOperation.CreateReadOnlySpanFromRva, RvaSpanLocalEliminatorTests.Data)
            .Local(CilOperation.StoreLocal, 0);

        // _ = span.Length, as inlining leaves it.
        public CilBuilder ReadLength() => code.Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.LoadField, new CilOperand.FieldInstance(RvaSpanLocalEliminatorTests.Length)).Op(CilOperation.Pop);
    }
}
