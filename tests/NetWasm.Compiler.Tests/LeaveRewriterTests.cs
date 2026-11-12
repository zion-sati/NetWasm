using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// leave empties the evaluation stack and exits any number of enclosing blocks, none included
// (ECMA-335 III.3.46; issue 221's class): each value it discards is popped before it, and one
// that exits no block becomes a branch. Every other leave, and a body without one, is unchanged.
public sealed class LeaveRewriterTests
{
    private static readonly EntityKey Exception = new(new AssemblyIdentity("fixture"), 0x02000001);

    [Fact]
    public void RejectsMissingCollaboratorsAndBodies()
    {
        var program = new Program();
        var validator = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
        Assert.Throws<ArgumentNullException>(() => new LeaveRewriter(null!, validator));
        Assert.Throws<ArgumentNullException>(() => new LeaveRewriter(new ControlFlowGraphBuilderFactory().Create(), null!));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    [Fact]
    public void LeavesABodyWithoutALeaveOrWithOnlyExitingLeavesOnEmptyStacksAsItIs()
    {
        var plain = Body([I(0, CilOperation.Return)], []);
        var exiting = Body(
            [I(0, CilOperation.Nop), I(1, CilOperation.Leave, 4), I(2, CilOperation.Nop), I(3, CilOperation.EndFinally),
                I(4, CilOperation.Return)],
            [new(CilExceptionRegionKind.Finally, 0, 2, 2, 2, null, null)]);

        Assert.Same(plain, Rewriter().Rewrite(plain));
        Assert.Same(exiting, Rewriter().Rewrite(exiting));
    }

    // A leave outside every block, or to another instruction of its own block, is a branch; the
    // values a leave discards are popped first, whether it exits a block or not.
    [Fact]
    public void PopsWhatALeaveDiscardsAndMakesOneThatExitsNothingABranch()
    {
        var outside = Body([I(0, CilOperation.Leave, 1), I(1, CilOperation.Return)], []);
        var discarding = Body(
        [
            I(0, CilOperation.LoadNull), I(1, CilOperation.Throw), I(2, CilOperation.LoadInt32, 7),
            I(3, CilOperation.Leave, 4), I(4, CilOperation.Return),
        ], [new(CilExceptionRegionKind.Catch, 0, 2, 2, 2, Exception, null)]);
        var within = Body(
        [
            I(0, CilOperation.LoadInt32, 1), I(1, CilOperation.Leave, 2), I(2, CilOperation.Leave, 5),
            I(3, CilOperation.Nop), I(4, CilOperation.EndFinally), I(5, CilOperation.Return),
        ], [new(CilExceptionRegionKind.Finally, 0, 3, 3, 2, null, null)]);

        Assert.Equal([(CilOperation.Branch, 1), (CilOperation.Return, -1)], Code(Rewriter().Rewrite(outside)));
        // The handler starts with the exception on the stack, so the leave discards it and the 7.
        Assert.Equal(
            [(CilOperation.LoadInt32, -1), (CilOperation.Pop, -1), (CilOperation.Pop, -1), (CilOperation.Leave, 6),
                (CilOperation.Return, -1)],
            Code(Rewriter().Rewrite(discarding)).Skip(2));
        Assert.Equal(
            [(CilOperation.LoadInt32, -1), (CilOperation.Pop, -1), (CilOperation.Branch, 3), (CilOperation.Leave, 6),
                (CilOperation.Nop, -1), (CilOperation.EndFinally, -1), (CilOperation.Return, -1)],
            Code(Rewriter().Rewrite(within)));
    }

    private static LeaveRewriter Rewriter()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program));
    }

    private static CilMethodBody Body(ImmutableArray<CilInstruction> instructions, ImmutableArray<CilExceptionRegion> regions) =>
        new(new MethodDefinitionModel(new(new AssemblyIdentity("fixture"), 0x06000001), new(new AssemblyIdentity("fixture"), 0x02000002),
                "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1), 2, [], instructions)
        {
            ExceptionRegions = regions,
        };

    private static CilInstruction I(int offset, CilOperation operation, int? operand = null) =>
        new(offset, offset + 1, operation, operation switch
        {
            CilOperation.LoadInt32 => new CilOperand.ConstantI4(operand!.Value),
            _ when operand is int target => new CilOperand.BranchTarget(target),
            _ => new CilOperand.None(),
        });

    private static IEnumerable<(CilOperation, int)> Code(CilMethodBody body) => body.Instructions.Select(instruction =>
        (instruction.Operation, instruction.Operand is CilOperand.BranchTarget target ? target.Offset : -1));

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
