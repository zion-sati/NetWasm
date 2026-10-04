using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NativeCallbackPlanBuilderTests
{
    [Fact]
    public void PlansCallbacksInCanonicalOrderWithStableSymbolsAndGetterIndices()
    {
        var second = Callback(2);
        var first = Callback(1);
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(second.CanonicalName, second)
            .Add(first.CanonicalName, first);
        var builder = CreateBuilder();

        var plan = builder.Build(
            callbacks,
            callbacks.Keys.ToHashSet(StringComparer.Ordinal),
            9);

        Assert.Equal([first, second], plan.Methods.Select(callback => callback.Method));
        Assert.Equal([9, 10], plan.Methods.Select(callback =>
            callback.GetterIndex.GetValueOrDefault().Value));
        Assert.Equal(
            ["__netwasm_native_callback_0", "__netwasm_native_callback_1"],
            plan.Methods.Select(callback => callback.NativeSymbol));
        Assert.Equal(
            ["__netwasm_native_callback_0", "__netwasm_native_callback_1"],
            plan.Methods.Select(callback => callback.RuntimeImportSymbol));
        Assert.Equal(
            ["__netwasm_application_callback_0", "__netwasm_application_callback_1"],
            plan.Methods.Select(callback => callback.ThunkExportName));
        Assert.Equal(
            ["__netwasm_callback_address_0", "__netwasm_callback_address_1"],
            plan.Methods.Select(callback => callback.GetterName));
        Assert.Same(plan.Methods[0], plan.ByMethodIdentity[first.CanonicalName]);
        Assert.All(plan.Methods, callback =>
        {
            Assert.Equal(
                callback.Method.Signature.ReturnSignatureType,
                callback.Abi.PhysicalSignature.ReturnSignatureType);
            Assert.True(callback.Method.Signature.ParameterSignatureTypes.SequenceEqual(
                callback.Abi.PhysicalSignature.ParameterSignatureTypes));
        });
    }

    [Fact]
    public void EmptyInputProducesTheSharedEmptyPlan()
    {
        Assert.Same(
            NativeCallbackPlan.Empty,
            CreateBuilder().Build(
                ImmutableDictionary<string, MethodInstanceModel>.Empty,
                ImmutableHashSet<string>.Empty,
                0));
    }

    [Fact]
    public void SeparatesNamedAndAddressedOwnership()
    {
        var namedOnly = NamedCallback(1, "named_only");
        var namedAndAddressed = NamedCallback(2, "named_and_addressed");
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(namedOnly.CanonicalName, namedOnly)
            .Add(namedAndAddressed.CanonicalName, namedAndAddressed);

        var plan = CreateBuilder().Build(
            callbacks,
            ImmutableHashSet.Create(
                StringComparer.Ordinal,
                namedAndAddressed.CanonicalName),
            19);

        var first = Assert.Single(plan.Methods.Where(callback =>
            callback.Method == namedOnly));
        Assert.Equal("named_only", first.NativeSymbol);
        Assert.Equal("__netwasm_named_callback_import_0", first.RuntimeImportSymbol);
        Assert.Equal("named_only", first.ThunkExportName);
        Assert.Null(first.GetterName);
        Assert.Null(first.GetterIndex);
        Assert.True(first.IsNamed);
        Assert.False(first.IsAddressTaken);

        var second = Assert.Single(plan.Methods.Where(callback =>
            callback.Method == namedAndAddressed));
        Assert.Equal("named_and_addressed", second.NativeSymbol);
        Assert.Equal("__netwasm_named_callback_import_1", second.RuntimeImportSymbol);
        Assert.Equal("named_and_addressed", second.ThunkExportName);
        Assert.Equal("__netwasm_callback_address_0", second.GetterName);
        Assert.Equal(19, second.GetterIndex.GetValueOrDefault().Value);
        Assert.True(second.IsNamed);
        Assert.True(second.IsAddressTaken);
        Assert.Equal([second], plan.AddressedMethods.ToArray());
    }

    [Fact]
    public void RejectsDuplicateNamedEntryPoints()
    {
        var first = NamedCallback(1, "duplicate");
        var second = NamedCallback(2, "duplicate");
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(first.CanonicalName, first)
            .Add(second.CanonicalName, second);

        var exception = Assert.Throws<CompilerException>(() =>
            CreateBuilder().Build(
                callbacks,
                ImmutableHashSet<string>.Empty,
                0));

        Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsNamedEntryPointCollidingWithGeneratedLinkerSymbol()
    {
        var callback = NamedCallback(1, "__netwasm_named_callback_import_0");
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(callback.CanonicalName, callback);

        var exception = Assert.Throws<CompilerException>(() =>
            CreateBuilder().Build(
                callbacks,
                ImmutableHashSet<string>.Empty,
                0));

        Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsInvalidInputsAndUnsupportedDeclarations()
    {
        var builder = CreateBuilder();
        var callback = Callback(1);
        var managed = callback with
        {
            Definition = callback.Definition with { NativeCallback = null },
        };

        Assert.Throws<ArgumentNullException>(() => builder.Build(
            null!, ImmutableHashSet<string>.Empty, 0));
        Assert.Throws<ArgumentNullException>(() => builder.Build(
            ImmutableDictionary<string, MethodInstanceModel>.Empty, null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(
            ImmutableDictionary<string, MethodInstanceModel>.Empty,
            ImmutableHashSet<string>.Empty,
            -1));
        var error = Assert.Throws<CompilerException>(() => builder.Build(
            ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                managed.CanonicalName,
                managed),
            ImmutableHashSet<string>.Empty,
            0));
        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
    }

    [Fact]
    public void RejectsDuplicateIdentitiesAndContradictoryAddressOwnership()
    {
        var builder = CreateBuilder();
        var callback = Callback(1);
        var duplicateIdentities = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add("first", callback)
            .Add("second", callback);
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(callback.CanonicalName, callback);

        Assert.Throws<CompilerException>(() => builder.Build(
            duplicateIdentities,
            ImmutableHashSet<string>.Empty,
            0));
        Assert.Throws<CompilerException>(() => builder.Build(
            callbacks,
            ImmutableHashSet.Create(StringComparer.Ordinal, "missing"),
            0));
        Assert.Throws<CompilerException>(() => builder.Build(
            callbacks,
            ImmutableHashSet<string>.Empty,
            0));
    }

    private static INativeCallbackPlanBuilder CreateBuilder() =>
        new[]
        {
            new NativeCallbackPlanBuilder(
                new NativeCallbackDeclarationValidator(),
                NativeAbiTestSupport.ScalarSignaturePlanner()),
        }.Cast<INativeCallbackPlanBuilder>().Single();

    private static MethodInstanceModel Callback(int token)
    {
        var assembly = new AssemblyIdentity("Callbacks");
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.I4,
            CliValueKind.NativeInt);
        var definition = new MethodDefinitionModel(
            new(assembly, token),
            new(assembly, 100),
            "Callback",
            true,
            signature,
            token)
        {
            NativeCallback = new([], null, false, false),
        };
        return new(
            definition,
            CliTypeIdentity.Named(assembly, "Tests", "Callbacks", false),
            [],
            signature);
    }

    private static MethodInstanceModel NamedCallback(int token, string entryPoint)
    {
        var callback = Callback(token);
        return callback with
        {
            Definition = callback.Definition with
            {
                NativeCallback = callback.Definition.NativeCallback! with
                {
                    EntryPoint = entryPoint,
                },
            },
        };
    }
}
