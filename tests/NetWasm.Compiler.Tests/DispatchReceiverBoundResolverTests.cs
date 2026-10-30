using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

public sealed class DispatchReceiverBoundResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Bounds");
    private static readonly CliTypeIdentity Owner = Type("Owner");
    private static readonly CliTypeIdentity Contract = Type("IContract");
    private static readonly CliTypeIdentity Specific = Type("ISpecific");
    private static readonly CliTypeIdentity Value = CliTypeIdentity.Named(Assembly, "Bounds", "Value", isValueType: true);

    public enum Producer
    {
        Local,
        InstanceReceiver,
        Parameter,
        Field,
        Cast,
        Construction,
        Constrained,
    }

    [Theory]
    [InlineData(Producer.Local)]
    [InlineData(Producer.InstanceReceiver)]
    [InlineData(Producer.Parameter)]
    [InlineData(Producer.Field)]
    [InlineData(Producer.Cast)]
    [InlineData(Producer.Construction)]
    [InlineData(Producer.Constrained)]
    public void BoundsTheReceiverByWhatPushesIt(Producer producer)
    {
        var expected = producer == Producer.InstanceReceiver ? Owner : Specific;
        var previous = producer switch
        {
            Producer.Local => Instruction(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Producer.InstanceReceiver => Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Producer.Parameter => Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(1)),
            Producer.Field => Instruction(0, CilOperation.LoadField, new CilOperand.FieldInstance(new(
                new FieldDefinitionModel(new(Assembly, 0x04000001), new(Assembly, 0x02000001), "field",
                    CliValueKind.ManagedReference, false), Owner, Specific))),
            Producer.Cast => Instruction(0, CilOperation.CastClass, new CilOperand.TypeIdentity(Specific)),
            Producer.Construction => Instruction(0, CilOperation.NewObject, new CilOperand.MethodInstance(
                Method(".ctor", Specific, isStatic: false))),
            _ => Instruction(0, CilOperation.Constrained, new CilOperand.TypeIdentity(Specific)),
        };

        Assert.Equal(expected, Resolve([previous, Call(1)], [Specific]));
    }

    [Fact]
    public void KnowsNoBoundWhereTheReceiverIsUnclear()
    {
        var local = Instruction(0, CilOperation.LoadLocal, new CilOperand.Index(0));
        // The receiver is not the last value pushed when the call takes arguments.
        Assert.Null(Resolve([local, Call(1)], [Specific], argument: true));
        // Control can enter the call with another receiver.
        Assert.Null(Resolve([Instruction(0, CilOperation.Branch, new CilOperand.BranchTarget(2)), local, Call(2)], [Specific]));
        Assert.Null(Resolve([local, Call(1)], [Specific], handlerAt: 1));
        Assert.Null(Resolve([local, Call(1)], [Specific], filterAt: 1));
        Assert.Null(Resolve([Call(0)], [Specific]));
        Assert.Null(Resolve([Instruction(0, CilOperation.Duplicate, new CilOperand.None()), Call(1)], [Specific]));
        // A value-type receiver is passed by address, and an open type is no closed bound.
        Assert.Null(Resolve([local, Call(1)], [Value]));
        Assert.Null(Resolve([local, Call(1)], [CliTypeIdentity.GenericParameter(true, 0)]));
        Assert.Null(Resolve([Instruction(0, CilOperation.LoadLocal, new CilOperand.Index(3)), Call(1)], [Specific]));
        Assert.Null(Resolve([Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(4)), Call(1)], [Specific]));
    }

    private static CliTypeIdentity? Resolve(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CliTypeIdentity> locals,
        bool argument = false,
        int? handlerAt = null,
        int? filterAt = null)
    {
        var caller = Method("Run", Owner, isStatic: false, parameters: [Specific]);
        var body = new CilMethodBody(caller.Definition, 2, [.. locals.Select(static type => type.StackKind)], instructions)
        {
            LocalSignatureTypes = locals,
            ExceptionRegions = handlerAt is null && filterAt is null
                ? []
                : [new(filterAt is null ? CilExceptionRegionKind.Catch : CilExceptionRegionKind.Filter,
                    0, 1, handlerAt ?? 0, 1, null, filterAt)],
        };
        var declaration = Method("Invoke", Contract, isStatic: false, parameters: argument ? [Specific] : []);
        return new DispatchReceiverBoundResolver(new FixedTypeOperands()).Resolve(
            new ReachabilityInstructionRequest(caller, body), instructions.Length - 1, declaration);
    }

    private static CilInstruction Call(int offset) =>
        Instruction(offset, CilOperation.CallVirtual, new CilOperand.None());

    private static CilInstruction Instruction(int offset, CilOperation operation, CilOperand operand) =>
        new(offset, offset + 1, operation, operand);

    private static MethodInstanceModel Method(
        string name,
        CliTypeIdentity declaringType,
        bool isStatic,
        ImmutableArray<CliTypeIdentity> parameters = default)
    {
        var signature = MethodSignatureModel.Create(CliTypeIdentity.Primitive("void", CliValueKind.Void),
            [.. parameters.IsDefault ? [] : parameters]);
        return new(new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), name, isStatic,
            signature, 1), declaringType, [], signature);
    }

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Named(Assembly, "Bounds", name, isValueType: false);

    private sealed class FixedTypeOperands : ITypeOperandResolver
    {
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) =>
            ((CilOperand.TypeIdentity)instruction.Operand).Value;
    }
}
