using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A walk is a do-while that the instruction before its head falls into: after
// a return, a throw or the end of a handler, nothing reaches the head but its
// own back edge.
public sealed class CountedWalkTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Reference = CliTypeIdentity.ManagedByReference(Int32);

    [Theory]
    [InlineData(CilOperation.Nop, true)]
    [InlineData(CilOperation.Return, false)]
    [InlineData(CilOperation.Throw, false)]
    [InlineData(CilOperation.Rethrow, false)]
    [InlineData(CilOperation.EndFinally, false)]
    [InlineData(CilOperation.EndFilter, false)]
    public void AWalkIsOneTheInstructionBeforeItsHeadFallsInto(CilOperation before, bool found) =>
        Assert.Equal(found ? 1 : 0, CountedWalks.Find(Walk(before), new OperandMethods()).Count());

    [Fact]
    public void TheWalkUnrollerRequiresItsResolver() =>
        Assert.Throws<ArgumentNullException>(() => new WalkLoopUnroller(null!, true));

    // before, then do { sum += *p; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0),
    // with p in local 0, sum in local 1 and n in local 2.
    private static CilMethodBody Walk(CilOperation before)
    {
        CilInstruction[] code =
        [
            I(before),
            I(CilOperation.LoadLocal, new CilOperand.Index(1)), I(CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)), I(CilOperation.Add),
            I(CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(CilOperation.LoadLocal, new CilOperand.Index(0)), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(CilOperation.Call, new CilOperand.MethodInstance(UnsafeAdd())), I(CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(CilOperation.LoadLocal, new CilOperand.Index(2)), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(CilOperation.Subtract), I(CilOperation.StoreLocal, new CilOperand.Index(2)),
            I(CilOperation.LoadLocal, new CilOperand.Index(2)), I(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
            I(CilOperation.Return),
        ];
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Walk", true,
            MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void)), 1);
        return new(method, 4, [CliValueKind.ManagedAddress, CliValueKind.I4, CliValueKind.I4],
            [.. code.Select(static (instruction, index) => instruction with { Offset = index, NextOffset = index + 1 })])
        {
            LocalSignatureTypes = [Reference, Int32, Int32],
        };
    }

    private static CilInstruction I(CilOperation operation, CilOperand? operand = null) =>
        new(0, 0, operation, operand ?? new CilOperand.None());

    // Unsafe.Add<int>(ref int, int).
    private static MethodInstanceModel UnsafeAdd()
    {
        var signature = MethodSignatureModel.Create(Reference, Reference, Int32);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "Add", true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "Unsafe", isValueType: false),
            ImmutableArray.Create(Int32), signature);
    }

    private sealed class OperandMethods : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }
}
