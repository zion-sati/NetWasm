using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ValueTypeHashReceiverResolverTests
{
    private static readonly CliTypeIdentity Value = CliTypeIdentity.Named(new("HashTests"), "Tests", "Value", true);

    [Fact]
    public void ConstrainedReceiverRetainsAddressRepresentation()
    {
        var dependency = new Types(Value);
        var resolver = new ValueTypeHashReceiverResolver(dependency, dependency);
        var request = Request() with { ConstrainedType = Value };
        Assert.Equal(new(Value, CliValueKind.ManagedAddress), Resolve(resolver, request));
        Assert.Equal(0, dependency.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AdjacentBoxPreservesReferenceRepresentation(int unrelatedBranch)
    {
        var dependency = new Types(Value);
        var resolver = new ValueTypeHashReceiverResolver(dependency, dependency);
        var request = Request();
        if (unrelatedBranch != 0)
        {
            var branch = new CilInstruction(20, 25, CilOperation.Branch,
                unrelatedBranch == 1 ? new CilOperand.BranchTarget(0) : new CilOperand.SwitchTargets([0]));
            request = WithBody(request, request.Instruction.Header.Instructions.Add(branch));
        }
        Assert.Equal(new(Value, CliValueKind.ManagedReference), Resolve(resolver, request));
        Assert.Equal(2, dependency.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void UnprovenOrChangedPayloadsAreRejected(int scenario)
    {
        var dependency = new Types(scenario == 7
            ? CliTypeIdentity.Named(new("HashTests"), "Tests", "Reference", false) : Value)
        { Nullable = scenario == 8 };
        var resolver = new ValueTypeHashReceiverResolver(dependency, dependency);
        var request = Request();
        var body = request.Instruction.Header.Instructions;
        request = scenario switch
        {
            0 => request with { Call = request.Call with { Consumed = 2 } },
            1 => request with { Call = request.Call with { Instruction = request.Instruction with { Stack = [CliValueKind.ManagedAddress] } } },
            2 => WithBody(request, [body[1]]),
            3 => WithBody(request, [body[0] with { Operation = CilOperation.Nop }, body[1]]),
            4 => WithBody(request, [body[0] with { NextOffset = 4 }, body[1]]),
            5 => WithBody(request, body.Add(new(20, 25, CilOperation.Branch, new CilOperand.BranchTarget(5)))),
            6 => WithBody(request, body.Add(new(20, 25, CilOperation.Switch, new CilOperand.SwitchTargets([5])))),
            _ => request,
        };
        Assert.Throws<InvalidOperationException>(() => Resolve(resolver, request));
    }

#pragma warning disable CA1859 // Exercise the receiver resolver through its capability contract.
    private static ValueTypeHashReceiver Resolve(IValueTypeHashReceiverResolver resolver, RuntimeIntrinsicEmissionRequest request) => resolver.Resolve(request);
#pragma warning restore CA1859

    private static RuntimeIntrinsicEmissionRequest Request()
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ValueTypeGetHashCode, [CliValueKind.ManagedReference]);
        var call = request.Instruction.Instruction with { Offset = 5, NextOffset = 10 };
        var producer = new CilInstruction(0, 5, CilOperation.Box, new CilOperand.TypeIdentity(Value));
        var instruction = request.Instruction with { Instruction = call, Header = request.Instruction.Header with { Instructions = [producer, call] } };
        return request with { Call = request.Call with { Instruction = instruction } };
    }

    private static RuntimeIntrinsicEmissionRequest WithBody(RuntimeIntrinsicEmissionRequest request, System.Collections.Immutable.ImmutableArray<CilInstruction> body) =>
        request with { Call = request.Call with { Instruction = request.Instruction with { Header = request.Instruction.Header with { Instructions = body } } } };

    private sealed class Types(CliTypeIdentity type) : ICilTypeOperandResolver, INullableTypeResolver
    {
        public bool Nullable { get; init; }
        public int Calls { get; private set; }
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? instance) { Calls++; return type; }
        public CliTypeIdentity? Resolve(CliTypeIdentity value) { Calls++; return Nullable ? type : null; }
    }
}
