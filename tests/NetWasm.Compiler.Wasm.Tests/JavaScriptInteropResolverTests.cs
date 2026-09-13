using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class JavaScriptInteropResolverTests
{
    [Theory]
    [InlineData(CliValueKind.I8, 8)]
    [InlineData(CliValueKind.F8, 8)]
    [InlineData(CliValueKind.I4, 4)]
    public void MapsFixedWidthResultDescriptors(
        CliValueKind resultKind,
        int expectedSize)
    {
        var resolver = EmitterTestSupport.CreateResultDescriptorSizes(
            new RecordingLayoutProvider());
        var parameterTypes = EmitterTestSupport.CreateImportParameterTypes(
            new RecordingLayoutProvider());
        var resultType = CliTypeIdentity.FromStackKind(resultKind);

        Assert.Equal(
            expectedSize,
            resolver.Resolve(resultType));
        Assert.Equal(resultKind, parameterTypes.Resolve(resultType, isCallback: false));
    }

    [Theory]
    [InlineData("JSObject")]
    [InlineData("JSSubscription")]
    public void HostObjectParametersUseJavaScriptHandles(string typeName)
    {
        var resolver = EmitterTestSupport.CreateImportParameterTypes(
            new RecordingLayoutProvider());
        var hostObject = CliTypeIdentity.Named(
            new AssemblyIdentity("System.Runtime.InteropServices.JavaScript"),
            "System.Runtime.InteropServices.JavaScript",
            typeName,
            isValueType: false);

        Assert.Equal(CliValueKind.I4, resolver.Resolve(hostObject, isCallback: false));
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 8)]
    public void MapsNativeIntegerDescriptorsAndParametersToTargetWidth(
        bool memory64,
        int expectedSize)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var descriptorSizes = EmitterTestSupport.CreateResultDescriptorSizes(layouts);
        var parameterTypes = EmitterTestSupport.CreateImportParameterTypes(layouts);
        var nativeInteger = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);

        Assert.Equal(expectedSize, descriptorSizes.Resolve(nativeInteger));
        Assert.Equal(
            memory64 ? CliValueKind.I8 : CliValueKind.I4,
            parameterTypes.Resolve(nativeInteger, isCallback: false));
        Assert.Equal(
            CliValueKind.I4,
            parameterTypes.Resolve(nativeInteger, isCallback: true));
    }
}
