using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Exceptions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Delegates;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class InstructionEmissionServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWasmInstructionEmissionRegistersTheCompleteInstructionGraph()
    {
        var services = new ServiceCollection();

        var result = services.AddWasmInstructionEmission();

        Assert.Same(services, result);

        var serviceTypes = services.Select(static descriptor => descriptor.ServiceType).ToHashSet();
        Type[] expected =
        [
            typeof(INativeIntegerConversionEmitter),
            typeof(ICilTypeIdentityResolver),
            typeof(ICilTypeOperandResolver),
            typeof(IArgumentTypeResolver),
            typeof(IArgumentSignatureTypeResolver),
            typeof(IImplicitExceptionEmitter),
            typeof(IExceptionPayloadBlockEmitter),
            typeof(IExceptionFieldLayoutResolver),
            typeof(IExceptionObjectStateReader),
            typeof(IManagedTerminalExceptionBoundaryEmitter),
            typeof(IInstructionCommandFactory),
            typeof(ICheckedBinaryEmitter),
            typeof(ConstantsStackEmitter),
            typeof(LocalsArgumentsEmitter),
            typeof(ExceptionAndReturnEmitter),
            typeof(StructuredControlFlowInstructionHandler),
            typeof(InstructionPrefixEmitter),
            typeof(NumericOperatorEmitter),
            typeof(NumericConversionInstructionEmitter),
            typeof(NumericComparisonInstructionEmitter),
            typeof(ExceptionalNumericInstructionEmitter),
            typeof(NumericInstructionEmitter),
            typeof(IInstructionDispatcherFactory),
            typeof(ICilInstructionDispatcher),
        ];

        Assert.All(expected, serviceType => Assert.Contains(serviceType, serviceTypes));
    }
}
