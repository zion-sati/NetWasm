using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NumericInstructionEmitterTests
{
    [Fact]
    public void ComposesEveryNumericOperationWithoutDuplicateOwnership()
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts, 7);
        var emitter = new NumericInstructionEmitter(
            new NumericOperatorEmitter(layouts),
            new NumericConversionInstructionEmitter(
                layouts,
                new NativeIntegerConversionEmitter(layouts),
                exceptions),
            new NumericComparisonInstructionEmitter(layouts, new StackTypeCompatibilityValidator()),
            new ExceptionalNumericInstructionEmitter(
                layouts,
                new CheckedBinaryEmitter(layouts.Target, exceptions),
                new FloatingRemainderEmitter(),
                exceptions));
        var commands = emitter.Commands;

        Assert.Equal(commands.Length, commands.Select(command => command.Operation).Distinct().Count());
        Assert.All(commands, command =>
            Assert.Equal(InstructionFamily.Numeric, command.Family));
        Assert.Equal(
            CreateInstructionCommands(_ => { })
                .Count(command => command.Family == InstructionFamily.Numeric),
            commands.Length);
    }
}
