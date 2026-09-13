using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Emission;

public interface IManagedWasmEmitterFactory
{
    IManagedModuleEmitter Create(
        ITypeRepository types,
        ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields,
        IMethodRepository methods,
        ISymbolFormatter symbols,
        ITypeClassifier typeClassifier,
        IRuntimeIntrinsicRegistry intrinsics,
        ITargetLayout targetLayout,
        IValueLayoutProvider valueLayouts,
        ITypeLayoutProvider typeLayouts,
        IInstanceFieldLayoutProvider instanceFields,
        IStaticFieldLayoutProvider staticFields,
        IStaticDataLayout staticData,
        IRuntimeObjectLayout runtimeObjects,
        IManagedExceptionObjectProvider exceptionObjects,
        ITypeDescriptorSource typeDescriptors);
}

public sealed class ManagedWasmEmitterFactory(
    IWasmModuleEmitterFactory modules) : IManagedWasmEmitterFactory
{
    public IManagedModuleEmitter Create(
        ITypeRepository types,
        ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields,
        IMethodRepository methods,
        ISymbolFormatter symbols,
        ITypeClassifier typeClassifier,
        IRuntimeIntrinsicRegistry intrinsics,
        ITargetLayout targetLayout,
        IValueLayoutProvider valueLayouts,
        ITypeLayoutProvider typeLayouts,
        IInstanceFieldLayoutProvider instanceFields,
        IStaticFieldLayoutProvider staticFields,
        IStaticDataLayout staticData,
        IRuntimeObjectLayout runtimeObjects,
        IManagedExceptionObjectProvider exceptionObjects,
        ITypeDescriptorSource typeDescriptors) => new ManagedWasmEmitter(
            types,
            typeDefinitions,
            fields,
            methods,
            symbols,
            typeClassifier,
            intrinsics,
            targetLayout,
            valueLayouts,
            typeLayouts,
            instanceFields,
            staticFields,
            staticData,
            runtimeObjects,
            exceptionObjects,
            typeDescriptors,
            modules);
}
