// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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

    [Fact]
    public void ResolvesTheRawInteropImportSeparatelyFromItsManagedEntry()
    {
        var program = new FakeProgram();
        var resolver = new FunctionIndexResolver(
            program,
            program,
            new FunctionIndexMap(
                [],
                [],
                [],
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(EntryKey, new(33)))
            {
                JSImportForwarders = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                    .Add(EntryKey, new(44)),
            });

        Assert.Equal(33, resolver.Resolve(new InteropImportKey(EntryKey)));
        Assert.Equal(44, resolver.Resolve(EntryKey));
    }

    [Fact]
    public void MissingInteropImportRetainsRuntimeContractDiagnostic()
    {
        var program = new FakeProgram();
        var resolver = new FunctionIndexResolver(program, program, new FunctionIndexMap([], [], [], []));

        var missing = Assert.Throws<CompilerException>(() =>
            resolver.Resolve(new InteropImportKey(EntryKey)));

        Assert.Equal(DiagnosticCode.RuntimeContract, missing.Diagnostic.Code);
        Assert.Contains("Test.Type::Run", missing.Diagnostic.Message);
        Assert.Contains("has no imported Wasm function", missing.Diagnostic.Message);
    }
}
