using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Delegates;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Exceptions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal static class InstructionEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmInstructionEmission(
        this IServiceCollection services)
    {
        services.AddSingleton<INativeIntegerConversionEmitter,
            NativeIntegerConversionEmitter>();
        services.AddWasmDelegateInstructions();
        services.AddWasmMemoryInstructions();
        services.AddWasmObjectInstructions();

        services.AddSingleton<ICilTypeIdentityResolver, CilTypeIdentityResolver>();
        services.AddSingleton<ICilTypeOperandResolver, CilTypeOperandResolver>();
        services.AddSingleton<IArgumentTypeResolver, ArgumentTypeResolver>();
        services.AddSingleton<IArgumentSignatureTypeResolver,
            ArgumentSignatureTypeResolver>();
        services.AddSingleton<IImplicitExceptionEmitter, ImplicitExceptionEmitter>();
        services.AddSingleton<IExceptionPayloadBlockEmitter,
            ExceptionPayloadBlockEmitter>();
        services.AddSingleton<IExceptionFieldLayoutResolver,
            ExceptionFieldLayoutResolver>();
        services.AddSingleton<IExceptionObjectStateReader, ExceptionObjectStateReader>();
        services.AddSingleton<IManagedTerminalExceptionBoundaryEmitter,
            ManagedTerminalExceptionBoundaryEmitter>();
        services.AddSingleton<IInstructionCommandFactory,
            InstructionCommandFactory>();
        services.AddSingleton<ICheckedBinaryEmitter, CheckedBinaryEmitter>();
        services.AddSingleton<ConstantsStackEmitter>();
        services.AddSingleton<LocalsArgumentsEmitter>();
        services.AddSingleton<ExceptionAndReturnEmitter>();
        services.AddSingleton<StructuredControlFlowInstructionHandler>();
        services.AddSingleton<InstructionPrefixEmitter>();
        services.AddSingleton<NumericOperatorEmitter>();
        services.AddSingleton<NumericConversionInstructionEmitter>();
        services.AddSingleton<NumericComparisonInstructionEmitter>();
        services.AddSingleton<ExceptionalNumericInstructionEmitter>();
        services.AddSingleton<NumericInstructionEmitter>();
        services.AddInstructionCommandProvider<ConstantsStackEmitter>();
        services.AddInstructionCommandProvider<LocalsArgumentsEmitter>();
        services.AddInstructionCommandProvider<ExceptionAndReturnEmitter>();
        services.AddInstructionCommandProvider<StructuredControlFlowInstructionHandler>();
        services.AddInstructionCommandProvider<InstructionPrefixEmitter>();
        services.AddInstructionCommandProvider<NumericInstructionEmitter>();

        services.AddSingleton<IInstructionDispatcherFactory,
            InstructionDispatcherFactory>();
        services.AddSingleton<ICilInstructionDispatcher>(provider =>
            provider.GetRequiredService<IInstructionDispatcherFactory>().Create());
        return services;
    }

}
