using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawCliCoreSignatureProjectorTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, RawCoreValueType.I32)]
    [InlineData(WasmTarget.Wasm64, RawCoreValueType.I64)]
    public void ProjectsEverySupportedCliKindUsingTheSelectedTarget(
        WasmTarget target,
        RawCoreValueType addressType)
    {
        var identity = new RawCanonicalImportIdentity("host", "member");
        var input = new RawCliFunctionImportSignature(identity,
        [
            CliValueKind.I4,
            CliValueKind.ValueType,
            CliValueKind.I8,
            CliValueKind.F4,
            CliValueKind.F8,
            CliValueKind.NativeInt,
            CliValueKind.ManagedReference,
            CliValueKind.ManagedAddress,
        ], CliValueKind.ManagedAddress);

        var result = Create().Project(input, target);

        Assert.Same(identity, result.Identity);
        Assert.Equal(
        [
            RawCoreValueType.I32,
            RawCoreValueType.I32,
            RawCoreValueType.I64,
            RawCoreValueType.F32,
            RawCoreValueType.F64,
            addressType,
            addressType,
            addressType,
        ], result.Parameters);
        Assert.Equal([addressType], result.Results);
    }

    [Fact]
    public void ProjectsVoidResultAsNoCoreResults()
    {
        var result = Create().Project(new(new("host", "member"), [], CliValueKind.Void), WasmTarget.Wasm32);

        Assert.Empty(result.Parameters);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void RequiresSignatureIdentityNamesParametersAndSupportedTarget()
    {
        var actor = Create();
        Assert.Throws<ArgumentNullException>(() => actor.Project(null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => actor.Project(
            new(null!, [], CliValueKind.Void), WasmTarget.Wasm32));
        Assert.Throws<ArgumentException>(() => actor.Project(
            new(new(" ", "member"), [], CliValueKind.Void), WasmTarget.Wasm32));
        Assert.Throws<ArgumentException>(() => actor.Project(
            new(new("host", " "), [], CliValueKind.Void), WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => actor.Project(
            new(new("host", "member"), default, CliValueKind.Void), WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() => actor.Project(
            new(new("host", "member"), [], CliValueKind.Void), (WasmTarget)99));
    }

    [Fact]
    public void RejectsVoidAndUnsupportedParameterKinds()
    {
        var actor = Create();
        Assert.Throws<CompilerException>(() => actor.Project(
            new(new("host", "member"), [CliValueKind.Void], CliValueKind.Void), WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => actor.Project(
            new(new("host", "member"), [CliValueKind.Unknown], CliValueKind.Void), WasmTarget.Wasm64));
    }

    [Fact]
    public void RejectsUnsupportedResultKind()
    {
        Assert.Throws<CompilerException>(() => Create().Project(
            new(new("host", "member"), [], CliValueKind.Unknown), WasmTarget.Wasm32));
    }

    private static IRawCliCoreSignatureProjector Create() =>
        Assert.IsAssignableFrom<IRawCliCoreSignatureProjector>(new RawCliCoreSignatureProjector());
}
