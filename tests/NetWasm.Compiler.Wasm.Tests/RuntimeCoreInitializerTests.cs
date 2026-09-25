using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Support;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeCoreInitializerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant)]
    public void InitializesRuntimeCoreAtTargetAddressWidth(
        WasmTarget target,
        byte addressOpcode)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        IRuntimeCoreInitializer initializer =
            EmitterTestSupport.CreateRuntimeCoreInitializer(layouts, imports);
        var code = new GeneratedFunctionWriterFactory().Create();

        initializer.Initialize(code, 512);

        var bytes = code.Snapshots.Read();
        Assert.Equal(addressOpcode, bytes[0]);
        Assert.Contains(
            (byte)imports.Resolve(RuntimeImportSymbol.Initialize),
            bytes);
    }

    [Fact]
    public void CapacityIncludesConstructedAndValueTypeDescriptors()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var initializer = new RuntimeCoreInitializer(
            new FixedDescriptorSource(),
            layouts,
            imports,
            new AddressInstructionEmitter(layouts));
        var code = new GeneratedFunctionWriterFactory().Create();

        ((IRuntimeCoreInitializer)initializer).Initialize(code, 512);

        Assert.Contains((byte)10, code.Snapshots.Read());
    }

    private sealed class FixedDescriptorSource : ITypeDescriptorSource
    {
        private static readonly CliTypeIdentity ValueType = CliTypeIdentity.Named(
            new AssemblyIdentity("Test"),
            "Test",
            "Value",
            isValueType: true);

        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];

        public ImmutableArray<ConstructedTypeDescriptorLayout>
            ConstructedTypeDescriptors => [new(ValueType, 7, 0, 4, 0, 0, null)];

        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors =>
            [new(ValueType, 9, 4, 4, 0, 0)];
    }
}
