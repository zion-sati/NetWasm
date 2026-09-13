using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FunctionIndexResolverTests
{
    [Fact]
    public void ResolvesDirectAndConstructedMethods()
    {
        var program = new FakeProgram();
        var resolver = new FunctionIndexResolver(
            program,
            program,
            new FunctionIndexMap(
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                    EntryKey, new(30)),
                ImmutableDictionary<string, WasmFunctionIndex>.Empty
                    .Add("constructed", new(31)),
                [],
                []));

        Assert.Equal(30, resolver.Resolve(EntryKey));
        Assert.Equal(31, resolver.Resolve("constructed"));
        var method = program.GetMethod(EntryKey);
        var callable = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            method.Signature);
        Assert.Equal(30, resolver.Resolve(callable));

        var constructedType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [CliTypeIdentity.Named(Assembly, "Test", "Argument", isValueType: false)]);
        var constructed = new MethodInstanceModel(
            method,
            constructedType,
            [],
            method.Signature);
        var constructedResolver = new FunctionIndexResolver(
            program,
            program,
            new FunctionIndexMap(
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty,
                ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                    constructed.CanonicalName,
                    new(32)),
                [],
                []));

        Assert.Equal(32, constructedResolver.Resolve(constructed));
    }

    [Fact]
    public void MissingMethodsRetainRuntimeContractDiagnostics()
    {
        var program = new FakeProgram();
        var resolver = new FunctionIndexResolver(
            program,
            program,
            new FunctionIndexMap(
                [],
                [],
                [],
                []));

        var direct = Assert.Throws<CompilerException>(() => resolver.Resolve(EntryKey));
        var constructed = Assert.Throws<CompilerException>(() => resolver.Resolve("missing"));

        Assert.Equal(DiagnosticCode.RuntimeContract, direct.Diagnostic.Code);
        Assert.Contains("Test.Type::Run", direct.Diagnostic.Message);
        Assert.Equal(DiagnosticCode.RuntimeContract, constructed.Diagnostic.Code);
        Assert.Contains("missing", constructed.Diagnostic.Message);
    }
}
