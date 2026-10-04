using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeBindingValidatorTests
{
    [Fact]
    public void CachedEvidenceReprovesCurrentProviderIdentityAndDefinedSignature()
    {
        var binding = Binding("sum");
        var evidence = new RuntimeValidatedNativeBinding("sum", binding.Provider.Path, binding.Provider.Sha256, "mule.o");
        var validator = Assert.IsAssignableFrom<IRuntimeNativeBindingValidator>(new RuntimeNativeBindingValidator());

        var result = Assert.Single(validator.Validate(new RuntimeNativeCachedBindingValidationRequest([binding], [evidence], Module([binding]))));

        Assert.Equal(evidence, result);
        Assert.Throws<InvalidOperationException>(() => validator.Validate(new RuntimeNativeCachedBindingValidationRequest(
            [binding], [evidence], Module([binding]) with { Exports = [] })));
    }

    [Theory]
    [InlineData("default-bindings")]
    [InlineData("default-evidence")]
    [InlineData("count")]
    [InlineData("null-binding")]
    [InlineData("null-import")]
    [InlineData("null-provider")]
    [InlineData("null-evidence")]
    [InlineData("entry")]
    [InlineData("path")]
    [InlineData("digest")]
    [InlineData("member")]
    public void RejectsIncompleteOrMismatchedCachedProvenance(string invalid)
    {
        var binding = Binding("sum");
        var evidence = new RuntimeValidatedNativeBinding("sum", binding.Provider.Path, binding.Provider.Sha256, "mule.o");
        var request = new RuntimeNativeCachedBindingValidationRequest([binding], [evidence], Module([binding]));
        request = invalid switch
        {
            "default-bindings" => request with { Bindings = default },
            "default-evidence" => request with { Evidence = default },
            "count" => request with { Evidence = [] },
            "null-binding" => request with { Bindings = [null!] },
            "null-import" => request with { Bindings = [binding with { Import = null! }] },
            "null-provider" => request with { Bindings = [binding with { Provider = null! }] },
            "null-evidence" => request with { Evidence = [null!] },
            "entry" => request with { Evidence = [evidence with { EntryPoint = "other" }] },
            "path" => request with { Evidence = [evidence with { ProviderPath = "/other/libmule.a" }] },
            "digest" => request with { Evidence = [evidence with { ProviderSha256 = new string('b', 64) }] },
            "member" => request with { Evidence = [evidence with { ArchiveMemberName = "" }] },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };

        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeBindingValidator().Validate(request));
    }

    [Fact]
    public void RejectsNullCachedValidationRequest() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeBindingValidator().Validate((RuntimeNativeCachedBindingValidationRequest)null!));

    [Fact]
    public void CachedEvidencePreservesInputBindingOrderAndRejectsNullModule()
    {
        var bindings = new[] { Binding("second"), Binding("first") };
        var evidence = bindings.Select(binding => new RuntimeValidatedNativeBinding(binding.Import.EntryPoint,
            binding.Provider.Path, binding.Provider.Sha256, binding.Import.EntryPoint + ".o")).ToImmutableArray();
        var validator = new RuntimeNativeBindingValidator();
        var request = new RuntimeNativeCachedBindingValidationRequest([.. bindings], evidence, Module(bindings));

        Assert.Equal(evidence.ToArray(), validator.Validate(request).ToArray());
        Assert.Throws<ArgumentNullException>(() => validator.Validate(request with { Module = null! }));
    }

    [Fact]
    public void ValidatesSelectedArchiveThenExactLtoDefinitionAndDefinedExports()
    {
        var provider = Provider();
        var bindings = new[]
        {
            Binding("sum", [RuntimeNativeValueType.I32, RuntimeNativeValueType.I32], RuntimeNativeValueType.I32, provider),
            Binding("identity", [RuntimeNativeValueType.I64], RuntimeNativeValueType.I64, provider),
            Binding("single", [RuntimeNativeValueType.F32], RuntimeNativeValueType.F32, provider),
            Binding("value", [RuntimeNativeValueType.F64], RuntimeNativeValueType.F64, provider),
            Binding("notify", [], null, provider),
        };
        var events = bindings.SelectMany(binding => new[]
        {
            Event(provider.Path + "(mule.o)", RuntimeLinkerSymbolEventKind.LazyDefinition, binding.Import.EntryPoint),
            Event("consumer.o", RuntimeLinkerSymbolEventKind.Reference, binding.Import.EntryPoint),
            Event(provider.Path + "(mule.o)", RuntimeLinkerSymbolEventKind.Definition, binding.Import.EntryPoint),
            Event("/output/runtime.wasm.lto.o", RuntimeLinkerSymbolEventKind.Definition, binding.Import.EntryPoint),
        }).ToImmutableArray();
        var validator = Assert.IsAssignableFrom<IRuntimeNativeBindingValidator>(new RuntimeNativeBindingValidator());

        var results = validator.Validate(new RuntimeNativeBindingValidationRequest([.. bindings], ["/output/runtime.wasm.lto.o"], events,
            Module(bindings)));

        Assert.Equal(bindings.Select(binding => binding.Import.EntryPoint), results.Select(result => result.EntryPoint));
        Assert.All(results, result =>
        {
            Assert.Equal(provider.Path, result.ProviderPath);
            Assert.Equal(provider.Sha256, result.ProviderSha256);
            Assert.Equal("mule.o", result.ArchiveMemberName);
        });
    }

    [Fact]
    public void ObjectDefinitionNeedsNoSyntheticDefinition()
    {
        var binding = Binding("sum");
        var result = Assert.Single(new RuntimeNativeBindingValidator().Validate(Request(binding,
            [Event(binding.Provider.Path + "(one.o)", RuntimeLinkerSymbolEventKind.Definition, "sum")])));
        Assert.Equal("one.o", result.ArchiveMemberName);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("lazy-only")]
    [InlineData("synthetic-only")]
    [InlineData("synthetic-first")]
    [InlineData("duplicate-original")]
    [InlineData("duplicate-synthetic")]
    [InlineData("original-after-synthetic")]
    [InlineData("wrong-provider")]
    [InlineData("unknown-generated")]
    [InlineData("empty-member")]
    [InlineData("newline-member")]
    public void RejectsUnprovenOrCompetingProvenance(string invalid)
    {
        var binding = Binding("sum");
        var original = Event(binding.Provider.Path + "(one.o)", RuntimeLinkerSymbolEventKind.Definition, "sum");
        var generated = Event("/output/runtime.wasm.lto.o", RuntimeLinkerSymbolEventKind.Definition, "sum");
        var events = invalid switch
        {
            "missing" => Array.Empty<RuntimeLinkerSymbolEvent>(),
            "lazy-only" => [original with { Kind = RuntimeLinkerSymbolEventKind.LazyDefinition }],
            "synthetic-only" => [generated],
            "synthetic-first" => [generated, original],
            "duplicate-original" => [original, original with { InputIdentity = binding.Provider.Path + "(two.o)" }],
            "duplicate-synthetic" => [original, generated, generated],
            "original-after-synthetic" => [original, generated, original],
            "wrong-provider" => [original with { InputIdentity = "/native/wrong.a(one.o)" }],
            "unknown-generated" => [original, generated with { InputIdentity = "/output/unknown.o" }],
            "empty-member" => [original with { InputIdentity = binding.Provider.Path + "()" }],
            "newline-member" => [original with { InputIdentity = binding.Provider.Path + "(one\ntwo.o)" }],
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeBindingValidator().Validate(
            Request(binding, [.. events], ["/output/runtime.wasm.lto.o"])));
    }

    [Theory]
    [InlineData("missing-export")]
    [InlineData("duplicate-export")]
    [InlineData("non-function-export")]
    [InlineData("import-reexport")]
    [InlineData("function-index")]
    [InlineData("type-index")]
    [InlineData("parameters")]
    [InlineData("results")]
    public void RejectsMissingImportedOrMismatchedOutputFunction(string invalid)
    {
        var binding = Binding("sum");
        var module = Module([binding]);
        module = invalid switch
        {
            "missing-export" => module with { Exports = [] },
            "duplicate-export" => module with { Exports = [.. module.Exports, module.Exports[0]] },
            "non-function-export" => module with { Exports = [module.Exports[0] with { Kind = 3 }] },
            "import-reexport" => module with { Exports = [module.Exports[0] with { Index = 0 }] },
            "function-index" => module with { Exports = [module.Exports[0] with { Index = 2 }] },
            "type-index" => module with { DefinedFunctionTypeIndices = [9] },
            "parameters" => module with { FunctionTypes = [module.FunctionTypes[0] with { Parameters = [0x7e] }] },
            "results" => module with { FunctionTypes = [module.FunctionTypes[0] with { Results = [] }] },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeBindingValidator().Validate(
            Request(binding, [Original(binding)], module: module)));
    }

    [Theory]
    [InlineData("null-request")]
    [InlineData("null-module")]
    [InlineData("default-bindings")]
    [InlineData("default-generated")]
    [InlineData("default-events")]
    [InlineData("default-types")]
    [InlineData("default-imports")]
    [InlineData("default-definitions")]
    [InlineData("default-exports")]
    [InlineData("null-binding")]
    [InlineData("null-import")]
    [InlineData("null-provider")]
    [InlineData("duplicate-binding")]
    [InlineData("duplicate-generated")]
    [InlineData("empty-generated")]
    [InlineData("newline-generated")]
    [InlineData("null-event")]
    [InlineData("unknown-event")]
    [InlineData("bad-event-input")]
    [InlineData("bad-event-entry")]
    [InlineData("bad-event-kind")]
    public void RejectsIncompleteOrContradictoryValidationRequests(string invalid)
    {
        var binding = Binding("sum");
        RuntimeNativeBindingValidationRequest? request = Request(binding, [Original(binding)]);
        request = invalid switch
        {
            "null-request" => null,
            "null-module" => request with { Module = null! },
            "default-bindings" => request with { Bindings = default },
            "default-generated" => request with { PermittedGeneratedInputIdentities = default },
            "default-events" => request with { Events = default },
            "default-types" => request with { Module = request.Module with { FunctionTypes = default } },
            "default-imports" => request with { Module = request.Module with { ImportedFunctionTypeIndices = default } },
            "default-definitions" => request with { Module = request.Module with { DefinedFunctionTypeIndices = default } },
            "default-exports" => request with { Module = request.Module with { Exports = default } },
            "null-binding" => request with { Bindings = [null!] },
            "null-import" => request with { Bindings = [binding with { Import = null! }] },
            "null-provider" => request with { Bindings = [binding with { Provider = null! }] },
            "duplicate-binding" => request with { Bindings = [binding, binding] },
            "duplicate-generated" => request with { PermittedGeneratedInputIdentities = ["lto.o", "lto.o"] },
            "empty-generated" => request with { PermittedGeneratedInputIdentities = [""] },
            "newline-generated" => request with { PermittedGeneratedInputIdentities = ["lto\no"] },
            "null-event" => request with { Events = [null!] },
            "unknown-event" => request with { Events = [Original(binding) with { EntryPoint = "other" }] },
            "bad-event-input" => request with { Events = [Original(binding) with { InputIdentity = " " }] },
            "bad-event-entry" => request with { Events = [Original(binding) with { EntryPoint = "" }] },
            "bad-event-kind" => request with { Events = [Original(binding) with { Kind = (RuntimeLinkerSymbolEventKind)99 }] },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        if (invalid is "null-request" or "null-module")
            Assert.Throws<ArgumentNullException>(() => new RuntimeNativeBindingValidator().Validate(request!));
        else
            Assert.Throws<InvalidOperationException>(() => new RuntimeNativeBindingValidator().Validate(request!));
    }

    [Fact]
    public void RejectsUnknownPhysicalValueType()
    {
        var binding = Binding("sum") with
        {
            Import = Binding("sum").Import with { Parameters = [(RuntimeNativeValueType)99] },
        };
        Assert.Throws<InvalidOperationException>(() => new RuntimeNativeBindingValidator().Validate(
            Request(binding, [Original(binding)])));
    }

    private static RuntimeNativeBindingValidationRequest Request(
        RuntimeNativeBinding binding,
        ImmutableArray<RuntimeLinkerSymbolEvent> events,
        ImmutableArray<string>? generated = null,
        RuntimeLinkedModule? module = null) =>
        new([binding], generated ?? [], events, module ?? Module([binding]));

    private static RuntimeNativeBinding Binding(
        string entry,
        ImmutableArray<RuntimeNativeValueType>? parameters = null,
        RuntimeNativeValueType? result = RuntimeNativeValueType.I32,
        RuntimeNativeLibrary? provider = null) =>
        new(new("mule", entry, parameters ?? [RuntimeNativeValueType.I32, RuntimeNativeValueType.I32], result),
            provider ?? Provider());

    private static RuntimeNativeLibrary Provider() =>
        new("mule", "wasm32", Path.GetFullPath("native/libmule.a"), RuntimePackTestData.Digest);

    private static RuntimeLinkerSymbolEvent Event(
        string input, RuntimeLinkerSymbolEventKind kind, string entry) => new(input, kind, entry);

    private static RuntimeLinkerSymbolEvent Original(RuntimeNativeBinding binding) =>
        Event(binding.Provider.Path + "(mule.o)", RuntimeLinkerSymbolEventKind.Definition, binding.Import.EntryPoint);

    private static RuntimeLinkedModule Module(IEnumerable<RuntimeNativeBinding> bindings)
    {
        var imports = ImmutableArray.Create<uint>(0);
        var types = bindings.Select(binding => new RuntimeLinkedFunctionType(
            [.. binding.Import.Parameters.Select(ValueType)],
            binding.Import.ReturnType is { } result ? [ValueType(result)] : [])).ToImmutableArray();
        var functions = Enumerable.Range(0, types.Length).Select(index => checked((uint)index)).ToImmutableArray();
        var exports = bindings.Select((binding, index) => new RuntimeLinkedExport(
            binding.Import.EntryPoint, 0, checked((uint)(index + imports.Length)))).ToImmutableArray();
        return new(new("wasm32", 0, 0, 0, 0, 0, 65_536, 1_048_576),
            types, imports, functions, exports);
    }

    private static byte ValueType(RuntimeNativeValueType value) => value switch
    {
        RuntimeNativeValueType.I32 => 0x7f,
        RuntimeNativeValueType.I64 => 0x7e,
        RuntimeNativeValueType.F32 => 0x7d,
        RuntimeNativeValueType.F64 => 0x7c,
        _ => 0,
    };
}
