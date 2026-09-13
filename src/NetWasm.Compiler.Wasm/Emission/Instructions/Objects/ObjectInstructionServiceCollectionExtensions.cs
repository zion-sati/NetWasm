using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal static class ObjectInstructionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmObjectInstructions(
        this IServiceCollection services)
    {
        services.AddSingleton<INullableTypeResolver, NullableTypeResolver>();
        services.AddSingleton<INullableBoxEmitter, NullableBoxEmitter>();
        services.AddSingleton<INullableUnboxAnyEmitter, NullableUnboxAnyEmitter>();
        services.AddSingleton<IBoxedValueTypeValidator, BoxedValueTypeValidator>();
        services.AddSingleton<IUnboxEmitter, BoxedValueUnboxEmitter>();
        services.AddSingleton<IStringConstructionPlanResolver,
            StringConstructionPlanResolver>();
        services.AddSingleton<IStringConstructionEmitter, StringConstructionEmitter>();
        services.AddSingleton<INativeIntegerConstructionEmitter,
            NativeIntegerConstructionEmitter>();
        services.AddInstructionCommandProvider<TypeMaterializationEmitter>();
        services.AddSingleton<IArrayLengthAdapter, ArrayLengthAdapter>();
        services.AddInstructionCommandProvider<ArrayInstructionEmitter>();
        services.AddSingleton<IRectangularArrayElementAddressEmitter,
            RectangularArrayElementAddressEmitter>();
        services.AddInstructionCommandProvider<RectangularArrayAllocationEmitter>();
        services.AddInstructionCommandProvider<RectangularArrayElementLoadEmitter>();
        services.AddInstructionCommandProvider<RectangularArrayElementStoreEmitter>();
        services.AddInstructionCommandProvider<
            RectangularArrayElementAddressInstructionEmitter>();
        services.AddInstructionCommandProvider<BoxingInstructionEmitter>();
        services.AddInstructionCommandProvider<TypeTestInstructionEmitter>();
        services.AddSingleton<ITypeTestEmitter>(provider =>
            provider.GetRequiredService<TypeTestInstructionEmitter>());
        services.AddInstructionCommandProvider<UnboxAnyInstructionEmitter>();
        services.AddInstructionCommandProvider<ObjectConstructionEmitter>();
        return services;
    }
}
