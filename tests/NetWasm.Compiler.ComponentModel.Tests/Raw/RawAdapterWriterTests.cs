using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawAdapterWriterTests
{
    private readonly RawAdapterWriter _subject = new(new WitInterfaceSpecifierFormatter());

    [Fact]
    public void RequiresInterfaceSpecifierFormatter()
    {
        Assert.Throws<ArgumentNullException>(() => new RawAdapterWriter(null!));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "wasm32")]
    [InlineData(WasmTarget.Wasm64, "wasm64")]
    public void WritesDeterministicDependencyFreeSelectedBindingFactory(
        WasmTarget target,
        string targetName)
    {
        var fixture = Fixture.Create(target);

        var first = _subject.Write(fixture.Validated);
        var second = _subject.Write(fixture.Validated);
        var source = Encoding.UTF8.GetString(first);
        var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(fixture.Document.NormalizedJson))).ToLowerInvariant();

        Assert.Equal(first, second);
        Assert.Equal((byte)'e', first[0]);
        Assert.EndsWith("\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', source);
        Assert.DoesNotContain("import ", source, StringComparison.Ordinal);
        Assert.Contains($"\"abiVersion\":{RawAdapterWriter.CurrentAbiVersion}", source, StringComparison.Ordinal);
        Assert.Contains($"\"target\":\"{targetName}\"", source, StringComparison.Ordinal);
        Assert.Contains($"\"witSourceFingerprint\":\"{fingerprint}\"", source, StringComparison.Ordinal);
        Assert.Contains("export function createAdapter(request = {})", source, StringComparison.Ordinal);
        Assert.Contains("request.metadata !== rawAdapterMetadata", source, StringComparison.Ordinal);
        Assert.Contains("request.bindCallable", source, StringComparison.Ordinal);
        Assert.Contains("request.bindResource", source, StringComparison.Ordinal);
        Assert.Contains("\"requiredCapabilities\":[\"bindCallable\",\"bindResource\"]", source,
            StringComparison.Ordinal);
        Assert.Contains($"\"module\":\"{fixture.Callable.Identity.Module}\"", source, StringComparison.Ordinal);
        Assert.Contains($"\"name\":\"{fixture.Callable.Identity.Name}\"", source, StringComparison.Ordinal);
        Assert.Contains("\"interface\":\"sample:raw/api@1.0.0\"", source,
            StringComparison.Ordinal);
        Assert.Contains("\"javascriptName\":\"readRecords\"", source, StringComparison.Ordinal);
        Assert.Contains("\"javascriptName\":\"fieldName\"", source, StringComparison.Ordinal);
        Assert.Contains("\"javascriptName\":\"FileHandle\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.JavaScript.Module, source, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Runtime.Module, source, StringComparison.Ordinal);
        Assert.DoesNotContain("unused-physical-member", source, StringComparison.Ordinal);
        Assert.Contains(target == WasmTarget.Wasm64
            ? "\"coreSignature\":{\"parameters\":[\"i64\",\"i64\"]"
            : "\"coreSignature\":{\"parameters\":[\"i32\",\"i32\"]", source, StringComparison.Ordinal);
        Assert.Contains("return Object.freeze({ metadata: rawAdapterMetadata, imports: Object.freeze(imports) });",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitsEverySupportedCanonicalTypeShape()
    {
        var source = Encoding.UTF8.GetString(_subject.Write(Fixture.Create(WasmTarget.Wasm32).Validated));

        foreach (var kind in new[]
        {
            "unit", "bool", "s8", "u8", "s16", "u16", "s32", "u32", "s64", "u64",
            "f32", "f64", "character", "text", "alias", "list", "record", "tuple", "option",
            "result", "variant", "enum", "flags", "owned-resource", "borrowed-resource",
        })
        {
            Assert.Contains($"\"kind\":\"{kind}\"", source, StringComparison.Ordinal);
        }
        Assert.Contains("\"count\":2", source, StringComparison.Ordinal);
        Assert.Contains("\"resourceType\":0", source, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"second-case\"", source, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"second-flag\",\"javascriptName\":\"secondFlag\"", source,
            StringComparison.Ordinal);
        Assert.Contains("\"discriminantSize\":", source, StringComparison.Ordinal);
        Assert.Contains("\"payloadOffset\":", source, StringComparison.Ordinal);
        Assert.Contains("\"indirectParameters\":true", source, StringComparison.Ordinal);
        Assert.Contains("\"indirectResult\":true", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("freestanding", null, "get-value", "getValue")]
    [InlineData("method", 9, "[method]file-handle.read-value", "readValue")]
    [InlineData("static", 9, "[static]file-handle.open-value", "openValue")]
    [InlineData("constructor", 9, "[constructor]file-handle", "FileHandle")]
    public void GeneratesPinnedJcoProviderNames(
        string kind,
        int? resourceType,
        string functionName,
        string javaScriptName)
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32, functionName, new(kind, resourceType));

        var source = Encoding.UTF8.GetString(_subject.Write(fixture.Validated));

        Assert.Contains($"\"function\":\"{functionName}\"", source, StringComparison.Ordinal);
        Assert.Contains($"\"javascriptName\":\"{javaScriptName}\"", source, StringComparison.Ordinal);
        Assert.Contains($"\"functionKind\":\"{kind}\"", source, StringComparison.Ordinal);
        Assert.Contains(resourceType is null
            ? "\"resourceJavaScriptName\":null"
            : "\"resourceJavaScriptName\":\"FileHandle\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CanonicalAbiFunctionKind.ImportedResourceDrop, "imported-resource-drop")]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceNew, "exported-resource-new")]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceRep, "exported-resource-rep")]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceDrop, "exported-resource-drop")]
    public void EmitsEachResourceIntrinsicAsABuiltIn(
        CanonicalAbiFunctionKind kind,
        string name)
    {
        var fixture = Fixture.Create(WasmTarget.Wasm64, resourceKind: kind);

        var source = Encoding.UTF8.GetString(_subject.Write(fixture.Validated));

        Assert.Contains($"\"intrinsic\":\"{name}\"", source, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"resource\"", source, StringComparison.Ordinal);
        Assert.Contains(kind == CanonicalAbiFunctionKind.ImportedResourceDrop
            ? "\"destructor\":null"
            : "\"destructor\":\"cm64p2|sample:raw/api@1|file-handle_dtor\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitsEveryIntrinsicForTheSameExportedResource()
    {
        var fixture = Fixture.Create(
            WasmTarget.Wasm32,
            resourceKind: CanonicalAbiFunctionKind.ExportedResourceNew);
        var representation = Fixture.ResourceLayout(
            fixture.Document,
            WasmTarget.Wasm32,
            CanonicalAbiFunctionKind.ExportedResourceRep);
        var drop = Fixture.ResourceLayout(
            fixture.Document,
            WasmTarget.Wasm32,
            CanonicalAbiFunctionKind.ExportedResourceDrop);

        var source = Encoding.UTF8.GetString(_subject.Write(
            fixture.WithLayouts(fixture.Resource, representation, drop)));

        Assert.Contains("\"requiredCapabilities\":[\"bindResource\"]", source,
            StringComparison.Ordinal);
        Assert.Contains("\"intrinsic\":\"exported-resource-new\"", source,
            StringComparison.Ordinal);
        Assert.Contains("\"intrinsic\":\"exported-resource-rep\"", source,
            StringComparison.Ordinal);
        Assert.Contains("\"intrinsic\":\"exported-resource-drop\"", source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmitsSelectedRuntimeReactorCallsThroughTheirDedicatedCapability(WasmTarget target)
    {
        var fixture = Fixture.Create(target);
        var watch = Fixture.ReactorLayout(fixture.Document, target, "watch");
        var cancel = Fixture.ReactorLayout(fixture.Document, target, "cancel");

        var source = Encoding.UTF8.GetString(_subject.Write(fixture.WithLayouts(watch, cancel)));

        Assert.Contains("\"requiredCapabilities\":[\"bindReactor\"]", source, StringComparison.Ordinal);
        Assert.Contains("bind(request.bindReactor, binding0)", source, StringComparison.Ordinal);
        Assert.Contains("bind(request.bindReactor, binding1)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("request.bindCallable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("request.bindResource", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnUnknownRuntimeReactorCallInsteadOfTreatingItAsAnApplicationProvider()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var unknown = Fixture.ReactorLayout(fixture.Document, WasmTarget.Wasm32, "other");

        var failure = Assert.Throws<CompilerException>(() =>
            _subject.Write(fixture.WithLayouts(unknown)));

        Assert.Contains("reactor-host function", failure.Diagnostic.Message, StringComparison.Ordinal);
        var ordinary = Fixture.Create(WasmTarget.Wasm32, functionName: "watch");
        Assert.Contains("request.bindCallable", Encoding.UTF8.GetString(_subject.Write(ordinary.Validated)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyWitSelectionRequiresNoBinderCapability()
    {
        var fixture = Fixture.Empty(WasmTarget.Wasm32);

        var source = Encoding.UTF8.GetString(_subject.Write(fixture.Validated));

        Assert.Contains("assertExactObject(request, [\"metadata\"]", source, StringComparison.Ordinal);
        Assert.Contains("\"requiredCapabilities\":[]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const binding0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("bindCallable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("bindResource", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesDisjointApplicationAndRuntimePlansIntoOneAdapter()
    {
        var application = Fixture.Create(WasmTarget.Wasm32, "application-call");
        var runtimeDocument = application.Document with
        {
            NormalizedJson = "{\"world\":\"runtime\"}",
        };
        var runtime = Fixture.Create(
            WasmTarget.Wasm32,
            "runtime-call",
            sourceDocument: runtimeDocument);

        var source = Encoding.UTF8.GetString(_subject.WriteDeployment(new(
        [
            application.WithLayouts(application.Callable),
            runtime.WithLayouts(runtime.Callable),
        ])));

        Assert.Contains("\"name\":\"application-call\"", source, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"runtime-call\"", source, StringComparison.Ordinal);
        Assert.Contains("bind(request.bindCallable, binding0)", source, StringComparison.Ordinal);
        Assert.Contains("bind(request.bindCallable, binding1)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GivesTheSameDeploymentTypeToTheSameResourceAcrossWitDocuments()
    {
        var application = Fixture.Create(WasmTarget.Wasm32, "application-call");
        var runtime = Fixture.Create(
            WasmTarget.Wasm32,
            "runtime-call",
            sourceDocument: Fixture.CreateShiftedResourceDocument(),
            resourceType: 12);

        var source = Encoding.UTF8.GetString(_subject.WriteDeployment(new(
        [
            application.WithLayouts(application.Callable),
            runtime.WithLayouts(runtime.Callable),
        ])));

        Assert.Contains("\"resourceType\":0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"resourceType\":9", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"resourceType\":12", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresAValidatedPlan()
    {
        Assert.Throws<ArgumentNullException>(() => _subject.Write(null!));
        Assert.Throws<ArgumentNullException>(() => _subject.WriteDeployment(null!));
        Assert.Throws<CompilerException>(() => _subject.WriteDeployment(new([])));
    }

    [Fact]
    public void RejectsNullPlansInEveryPosition()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);

        Assert.Throws<CompilerException>(() =>
            _subject.WriteDeployment(new([null!])));
        Assert.Throws<CompilerException>(() =>
            _subject.WriteDeployment(new([fixture.Validated, null!])));
    }

    [Fact]
    public void RejectsMixedTargetsAndDuplicatePhysicalIdentities()
    {
        var wasm32 = Fixture.Create(WasmTarget.Wasm32);
        var wasm64 = Fixture.Create(WasmTarget.Wasm64);

        Assert.Throws<CompilerException>(() =>
            _subject.WriteDeployment(new([wasm32.Validated, wasm64.Validated])));
        Assert.Throws<CompilerException>(() =>
            _subject.WriteDeployment(new([wasm32.Validated, wasm32.Validated])));
    }

    [Fact]
    public void RejectsBindingWithoutValidatedFinalSignature()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var expected = fixture.Validated.ExpectedImports
            .Where(signature => signature.Identity != fixture.Callable.Identity)
            .ToImmutableArray();

        Assert.Throws<CompilerException>(() => _subject.Write(new(
            fixture.Plan,
            expected,
            fixture.Validated.ObservedImports)));
    }

    [Theory]
    [InlineData("negative-id")]
    [InlineData("id-out-of-range")]
    [InlineData("id-mismatch")]
    [InlineData("blank-name")]
    [InlineData("missing-owner")]
    [InlineData("negative-owner")]
    [InlineData("owner-out-of-range")]
    public void RejectsInvalidDeploymentResourceIdentity(string defect)
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var document = fixture.Document;
        var resource = document.Types[9];
        resource = defect switch
        {
            "negative-id" => resource with { Id = -1 },
            "id-out-of-range" => resource with { Id = document.Types.Length },
            "id-mismatch" => resource with { Id = 0 },
            "blank-name" => resource with { Name = " " },
            "missing-owner" => resource with { OwnerInterface = null },
            "negative-owner" => resource with { OwnerInterface = -1 },
            "owner-out-of-range" => resource with { OwnerInterface = document.Interfaces.Length },
            _ => throw new InvalidOperationException(),
        };
        document = document with { Types = document.Types.SetItem(9, resource) };

        Assert.Throws<CompilerException>(() => _subject.Write(WithDocument(fixture, document)));
    }

    [Fact]
    public void RejectsMissingDeploymentResourceOwner()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var document = fixture.Document with { Interfaces = [null!] };

        Assert.Throws<CompilerException>(() => _subject.Write(WithDocument(fixture, document)));
    }

    [Fact]
    public void RejectsLayoutWhoseResourceHasNoDeploymentIdentity()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        using var json = JsonDocument.Parse("\"u32\"");
        var document = fixture.Document with
        {
            Types = fixture.Document.Types.SetItem(
                9,
                fixture.Document.Types[9] with { Kind = json.RootElement.Clone() }),
        };

        Assert.Throws<CompilerException>(() => _subject.Write(WithDocument(fixture, document)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("callable-as-resource")]
    [InlineData("resource-as-callable")]
    [InlineData("blank-interface")]
    public void RejectsLayoutWithoutItsSelectedDeclaration(string defect)
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var imports = fixture.Catalog.Imports.ToBuilder();
        var callable = Assert.IsType<RawWitImportDeclaration.Callable>(
            imports[fixture.Callable.Identity]);
        var resource = Assert.IsType<RawWitImportDeclaration.Resource>(
            imports[fixture.Resource.Identity]);
        switch (defect)
        {
            case "missing":
                imports.Remove(fixture.Callable.Identity);
                break;
            case "callable-as-resource":
                imports[fixture.Callable.Identity] = resource;
                break;
            case "resource-as-callable":
                imports[fixture.Resource.Identity] = callable;
                break;
            case "blank-interface":
                imports[fixture.Callable.Identity] = callable with
                {
                    DeploymentInterfaceName = " ",
                };
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Throws<CompilerException>(() => _subject.Write(
            WithCatalog(fixture, fixture.Catalog with { Imports = imports.ToImmutable() })));
    }

    [Fact]
    public void RejectsResourceMemberWhoseLogicalOwnerDiffersFromItsWitType()
    {
        var fixture = Fixture.Create(
            WasmTarget.Wasm32,
            "[method]other-resource.read-value",
            new("method", 9));

        Assert.Throws<CompilerException>(() => _subject.Write(fixture.Validated));
    }

    [Theory]
    [InlineData("unsupported", null, "operation")]
    [InlineData("freestanding", 9, "operation")]
    [InlineData("method", null, "[method]file-handle.read")]
    [InlineData("method", 9, "[static]file-handle.read")]
    [InlineData("method", 9, "[method]")]
    [InlineData("method", 9, "[method]file-handle")]
    [InlineData("method", 9, "[method]file-handle.")]
    [InlineData("constructor", 9, "[constructor]file-handle.create")]
    [InlineData("method", -1, "[method]file-handle.read")]
    [InlineData("method", 99, "[method]file-handle.read")]
    [InlineData("method", 7, "[method]mode.read")]
    [InlineData("freestanding", null, "-")]
    public void RejectsUnsupportedJcoProviderIdentity(
        string kind,
        int? resourceType,
        string functionName)
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var callable = Assert.IsType<RawWitImportLayout.Callable>(fixture.Callable);
        var changed = new RawWitImportLayout.Callable(callable.Function with
        {
            Declaration = callable.Function.Declaration with
            {
                Name = functionName,
                Kind = new(kind, resourceType),
            },
        });

        Assert.Throws<CompilerException>(() => _subject.Write(fixture.WithLayouts(changed, fixture.Resource)));
    }

    [Fact]
    public void RejectsProviderNamesThatCollideAfterJcoMapping()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var collision = Fixture.CallableLayout(
            fixture.Document,
            WasmTarget.Wasm32,
            "read--records",
            new("freestanding"));

        Assert.Throws<CompilerException>(() => _subject.Write(
            fixture.WithLayouts(fixture.Callable, collision, fixture.Resource)));
    }

    [Fact]
    public void RejectsRecordAndFlagNamesThatCollideAfterJcoMapping()
    {
        var record = Fixture.CreateWithType(
            2,
            "record",
            "{\"record\":{\"fields\":[{\"name\":\"field-name\",\"type\":\"u16\"}," +
            "{\"name\":\"field--name\",\"type\":\"u16\"}]}}");
        var flags = Fixture.CreateWithType(
            8,
            "options",
            "{\"flags\":{\"flags\":[{\"name\":\"first-flag\"}," +
            "{\"name\":\"first--flag\"}]}}");

        Assert.Throws<CompilerException>(() => _subject.Write(record.Validated));
        Assert.Throws<CompilerException>(() => _subject.Write(flags.Validated));
    }

    [Fact]
    public void RejectsResourceClassNamesThatCollideAfterJcoMapping()
    {
        var fixture = Fixture.CreateWithAdditionalResource(12, "file--handle");
        var collision = Fixture.ResourceLayout(
            fixture.Document,
            WasmTarget.Wasm32,
            CanonicalAbiFunctionKind.ImportedResourceDrop,
            12);

        Assert.Throws<CompilerException>(() => _subject.Write(
            fixture.WithLayouts(fixture.Callable, fixture.Resource, collision)));
    }

    [Fact]
    public void EmitsCallableWithoutResultOrResultMemory()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var callable = Assert.IsType<RawWitImportLayout.Callable>(fixture.Callable);
        var noResult = new RawWitImportLayout.Callable(callable.Function with
        {
            Declaration = callable.Function.Declaration with { Result = null },
            Layout = callable.Function.Layout with
            {
                Function = callable.Function.Layout.Function with { Result = null },
                ResultMemory = null,
            },
        });

        var source = Encoding.UTF8.GetString(_subject.Write(
            fixture.WithLayouts(noResult, fixture.Resource)));

        Assert.Contains("\"result\":null", source, StringComparison.Ordinal);
        Assert.Contains("\"resultMemory\":null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitsDirectCanonicalResultAndRejectsUnknownInternalKinds()
    {
        var fixture = Fixture.Create(WasmTarget.Wasm32);
        var callable = Assert.IsType<RawWitImportLayout.Callable>(fixture.Callable);
        var direct = new RawWitImportLayout.Callable(callable.Function with
        {
            Layout = callable.Function.Layout with
            {
                Signature = callable.Function.Layout.Signature with { Result = CliValueKind.I4 },
            },
        });
        Assert.Contains("\"result\":\"i32\"", Encoding.UTF8.GetString(
            _subject.Write(fixture.WithLayouts(direct, fixture.Resource))), StringComparison.Ordinal);

        var unknownCli = new RawWitImportLayout.Callable(callable.Function with
        {
            Layout = callable.Function.Layout with
            {
                Signature = callable.Function.Layout.Signature with { Parameters = [(CliValueKind)int.MaxValue] },
            },
        });
        Assert.Throws<CompilerException>(() => _subject.Write(
            fixture.WithUncheckedLayouts(unknownCli, fixture.Resource)));

        var unknownType = new RawWitImportLayout.Callable(callable.Function with
        {
            Layout = callable.Function.Layout with
            {
                Function = callable.Function.Layout.Function with
                {
                    Parameters = callable.Function.Layout.Function.Parameters.SetItem(0,
                        callable.Function.Layout.Function.Parameters[0] with
                        {
                            Type = callable.Function.Layout.Function.Parameters[0].Type with
                            {
                                Kind = (CanonicalAbiTypeKind)int.MaxValue,
                            },
                        }),
                },
            },
        });
        Assert.Throws<CompilerException>(() => _subject.Write(fixture.WithLayouts(unknownType, fixture.Resource)));

        var resource = Assert.IsType<RawWitImportLayout.Resource>(fixture.Resource);
        var unknownIntrinsic = new RawWitImportLayout.Resource(resource.Intrinsic with
        {
            Declaration = resource.Intrinsic.Declaration with { Kind = CanonicalAbiFunctionKind.Function },
        });
        Assert.Throws<CompilerException>(() => _subject.Write(fixture.WithLayouts(fixture.Callable, unknownIntrinsic)));

        var invalidCore = fixture.Validated.ExpectedImports.SetItem(0,
            fixture.Validated.ExpectedImports[0] with { Results = [(RawCoreValueType)int.MaxValue] });
        Assert.Throws<CompilerException>(() => _subject.Write(new(
            fixture.Plan,
            invalidCore,
            fixture.Validated.ObservedImports)));
    }

    private static RawValidatedBindingPlan WithDocument(
        Fixture fixture,
        WitDocument document) =>
        WithCatalog(fixture, fixture.Catalog with { Document = document });

    private static RawValidatedBindingPlan WithCatalog(
        Fixture fixture,
        RawWitImportCatalog catalog)
    {
        var selection = fixture.Selection with { Catalog = catalog };
        var plan = fixture.Plan with { Selection = selection };
        return new(plan, fixture.Validated.ExpectedImports, fixture.Validated.ObservedImports);
    }

    private sealed record Fixture(
        WitDocument Document,
        RawWitImportCatalog Catalog,
        RawImportBindingSelection Selection,
        RawWitBindingPlan Plan,
        RawValidatedBindingPlan Validated,
        RawWitImportLayout Callable,
        RawWitImportLayout Resource,
        RawCanonicalImportIdentity JavaScript,
        RawCanonicalImportIdentity Runtime)
    {
        private static readonly ImmutableArray<string> PrimitiveTypeNames =
        [
            "bool", "s8", "u8", "s16", "u16", "s32", "u32", "s64", "u64", "f32", "f64",
            "char", "string",
        ];

        public static Fixture Create(
            WasmTarget target,
            string functionName = "read-records",
            WitFunctionKind? functionKind = null,
            CanonicalAbiFunctionKind resourceKind = CanonicalAbiFunctionKind.ImportedResourceDrop,
            WitDocument? sourceDocument = null,
            int resourceType = 9)
        {
            var document = sourceDocument ?? CreateDocument();
            var world = new WitWorld(0, "sample", "sample:raw@1.0.0", [], []);
            var callable = CallableLayout(document, target, functionName, functionKind ?? new("freestanding"));
            var resource = ResourceLayout(document, target, resourceKind, resourceType);
            var unused = new RawCanonicalImportIdentity(callable.Identity.Module, "unused-physical-member");
            var callableDeclaration = new RawWitImportDeclaration.Callable(
                callable.Function.Layout.Function.InterfaceName,
                "sample:raw/api@1.0.0",
                callable.Function.Declaration);
            var imports = ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty
                .Add(callable.Identity, callableDeclaration)
                .Add(resource.Identity, resource.Intrinsic.Declaration)
                .Add(unused, callableDeclaration);
            var catalog = new RawWitImportCatalog(document, world, target, imports);
            var javaScript = new RawCanonicalImportIdentity("javascript-boundary", "invoke");
            var runtime = new RawCanonicalImportIdentity("runtime-boundary", "exit");
            var selection = new RawImportBindingSelection(
                catalog,
                [callable.Identity, resource.Identity],
                [javaScript],
                [runtime]);
            var layouts = ImmutableArray.Create<RawWitImportLayout>(callable, resource);
            var plan = new RawWitBindingPlan(selection, layouts);
            var expected = ImmutableArray.Create(
                Signature(callable.Identity, callable.Signature, target),
                Signature(resource.Identity, resource.Signature, target),
                new RawCoreFunctionImportSignature(javaScript, [RawCoreValueType.I32], []),
                new RawCoreFunctionImportSignature(runtime, [], []));
            var observed = ImmutableArray.Create(expected[2], expected[0], expected[3], expected[1]);
            return new(document, catalog, selection, plan, new(plan, expected, observed),
                callable, resource, javaScript, runtime);
        }

        public static Fixture Empty(WasmTarget target)
        {
            var document = new WitDocument([], [], [], [], "{}");
            var world = new WitWorld(0, "sample", "sample:raw@1.0.0", [], []);
            var catalog = new RawWitImportCatalog(
                document,
                world,
                target,
                ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Empty);
            var selection = new RawImportBindingSelection(catalog, [], [], []);
            var plan = new RawWitBindingPlan(selection, []);
            return new(document, catalog, selection, plan, new(plan, [], []), null!, null!, null!, null!);
        }

        public static Fixture CreateWithType(int id, string name, string source)
        {
            var document = CreateDocument();
            document = document with { Types = document.Types.SetItem(id, TypeDefinition(id, name, source)) };
            return Create(WasmTarget.Wasm32, sourceDocument: document);
        }

        public static Fixture CreateWithAdditionalResource(int id, string name)
        {
            var document = CreateDocument();
            document = document with { Types = document.Types.Add(TypeDefinition(id, name, "\"resource\"")) };
            return Create(WasmTarget.Wasm32, sourceDocument: document);
        }

        public RawValidatedBindingPlan WithLayouts(params RawWitImportLayout[] layouts)
        {
            var immutable = layouts.ToImmutableArray();
            var imports = Catalog.Imports.ToBuilder();
            foreach (var layout in immutable)
            {
                if (!imports.ContainsKey(layout.Identity))
                {
                    imports.Add(layout.Identity, Declaration(layout));
                }
            }
            var catalog = Catalog with { Imports = imports.ToImmutable() };
            var selection = Selection with
            {
                Catalog = catalog,
                WitImports = [.. immutable.Select(layout => layout.Identity)],
            };
            var plan = Plan with { Selection = selection, Imports = immutable };
            var expected = immutable.Select(layout =>
                    Signature(layout.Identity, layout.Signature, selection.Catalog.Target))
                .Append(new RawCoreFunctionImportSignature(JavaScript, [RawCoreValueType.I32], []))
                .Append(new RawCoreFunctionImportSignature(Runtime, [], []))
                .ToImmutableArray();
            return new(plan, expected, expected);
        }

        public RawValidatedBindingPlan WithUncheckedLayouts(params RawWitImportLayout[] layouts)
        {
            var immutable = layouts.ToImmutableArray();
            var selection = Selection with { WitImports = [.. immutable.Select(layout => layout.Identity)] };
            var plan = Plan with { Selection = selection, Imports = immutable };
            return new(plan, Validated.ExpectedImports, Validated.ObservedImports);
        }

        private static RawWitImportDeclaration Declaration(RawWitImportLayout layout) =>
            layout switch
            {
                RawWitImportLayout.Callable callable => new RawWitImportDeclaration.Callable(
                    callable.Function.Layout.Function.InterfaceName,
                    DeploymentInterface(callable.Function.Layout.Function.InterfaceName),
                    callable.Function.Declaration),
                RawWitImportLayout.Resource resource => resource.Intrinsic.Declaration,
                _ => throw new InvalidOperationException(),
            };

        private static string DeploymentInterface(string interfaceName) =>
            interfaceName switch
            {
                "sample:raw@1.0.0/api" => "sample:raw/api@1.0.0",
                "netwasm:runtime@1.0.0/reactor-host" =>
                    "netwasm:runtime/reactor-host@1.0.0",
                _ => interfaceName,
            };

        internal static RawWitImportLayout.Callable CallableLayout(
            WitDocument document,
            WasmTarget target,
            string functionName,
            WitFunctionKind functionKind)
        {
            var references = AllReferences();
            var resolver = new WitCanonicalTypeResolver();
            var types = references.Select(reference => resolver.Resolve(document, reference)).ToImmutableArray();
            var parameters = types.Select((type, index) =>
                new CanonicalAbiParameter(index == 16 ? "field-name" : $"value-{index}", type)).ToImmutableArray();
            var resultReference = new WitTypeReference.Defined(4);
            var result = resolver.Resolve(document, resultReference);
            var canonical = new CanonicalAbiFunction(
                "sample:raw@1.0.0/api",
                functionName,
                default,
                parameters,
                result);
            var layout = new RawCanonicalFunctionLayoutPlanner(
                new RawCanonicalImportIdentityFormatter(),
                new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()),
                new CanonicalAbiMemoryLayoutPlanner()).Plan(canonical, target);
            var declaration = new WitFunction(functionName,
                [.. references.Select((reference, index) =>
                    new WitParameter(index == 16 ? "field-name" : $"value-{index}", reference))],
                resultReference,
                functionKind);
            return new(new(declaration, layout));
        }

        internal static RawWitImportLayout.Resource ResourceLayout(
            WitDocument document,
            WasmTarget target,
            CanonicalAbiFunctionKind kind,
            int resourceType = 9)
        {
            var declaration = new RawWitImportDeclaration.Resource(
                "sample:raw@1.0.0/api",
                "sample:raw/api@1.0.0",
                document.Types[resourceType],
                kind);
            var intrinsic = new RawResourceIntrinsicLayoutPlanner(
                new WitCanonicalTypeResolver(),
                new RawCanonicalImportIdentityFormatter(),
                new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()))
                .Plan(document, declaration, target);
            return new(intrinsic);
        }

        internal static RawWitImportLayout.Callable ReactorLayout(
            WitDocument document,
            WasmTarget target,
            string operation)
        {
            var references = operation == "watch"
                ? ImmutableArray.Create<WitTypeReference>(
                    new WitTypeReference.Defined(10),
                    new WitTypeReference.Primitive("u32"))
                : [new WitTypeReference.Primitive("u32")];
            var resolver = new WitCanonicalTypeResolver();
            var parameters = references.Select((reference, index) => new CanonicalAbiParameter(
                operation == "watch" && index == 0 ? "ready" : "token",
                resolver.Resolve(document, reference))).ToImmutableArray();
            var canonical = new CanonicalAbiFunction(
                "netwasm:runtime@1.0.0/reactor-host",
                operation,
                default,
                parameters,
                null);
            var layout = new RawCanonicalFunctionLayoutPlanner(
                new RawCanonicalImportIdentityFormatter(),
                new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()),
                new CanonicalAbiMemoryLayoutPlanner()).Plan(canonical, target);
            var declaration = new WitFunction(
                operation,
                [.. references.Select((reference, index) => new WitParameter(
                    operation == "watch" && index == 0 ? "ready" : "token",
                    reference))],
                null,
                new("freestanding"));
            return new(new(declaration, layout));
        }

        private static ImmutableArray<WitTypeReference> AllReferences()
        {
            var simple = PrimitiveTypeNames.Select(name =>
                (WitTypeReference)new WitTypeReference.Primitive(name));
            return
            [
                .. simple,
                .. Enumerable.Range(0, 12).Select(id =>
                    (WitTypeReference)new WitTypeReference.Defined(id)),
            ];
        }

        private static WitDocument CreateDocument() => new(
            [],
            [new WitInterface(
                0,
                "api",
                "sample:raw@1.0.0",
                ImmutableDictionary<string, int>.Empty,
                [])],
            [],
            [
                TypeDefinition(0, "alias", "{\"type\":1}"),
                TypeDefinition(1, "bytes", "{\"list\":\"u8\"}"),
                TypeDefinition(2, "record", "{\"record\":{\"fields\":[{\"name\":\"field-name\",\"type\":\"u16\"}]}}"),
                TypeDefinition(3, "tuple", "{\"tuple\":{\"types\":[\"f32\"]}}"),
                TypeDefinition(4, "optional", "{\"option\":\"s64\"}"),
                TypeDefinition(5, "outcome", "{\"result\":{\"ok\":\"string\",\"err\":null}}"),
                TypeDefinition(6, "choice", "{\"variant\":{\"cases\":[{\"name\":\"first-case\",\"type\":\"u8\"},{\"name\":\"second-case\",\"type\":null}]}}"),
                TypeDefinition(7, "mode", "{\"enum\":{\"cases\":[{\"name\":\"first-case\"},{\"name\":\"second-case\"}]}}"),
                TypeDefinition(8, "options", "{\"flags\":{\"flags\":[{\"name\":\"first-flag\"},{\"name\":\"second-flag\"}]}}"),
                TypeDefinition(9, "file-handle", "\"resource\""),
                TypeDefinition(10, null, "{\"handle\":{\"own\":9}}"),
                TypeDefinition(11, null, "{\"handle\":{\"borrow\":9}}"),
            ],
            "{\"world\":\"sample\"}");

        public static WitDocument CreateShiftedResourceDocument()
        {
            var document = CreateDocument();
            return document with
            {
                Types = document.Types
                    .SetItem(9, TypeDefinition(9, "retired", "{\"type\":\"u32\"}"))
                    .SetItem(10, TypeDefinition(10, null, "{\"handle\":{\"own\":12}}"))
                    .SetItem(11, TypeDefinition(11, null, "{\"handle\":{\"borrow\":12}}"))
                    .Add(TypeDefinition(12, "file-handle", "\"resource\"")),
                NormalizedJson = "{\"world\":\"shifted\"}",
            };
        }

        private static WitTypeDefinition TypeDefinition(int id, string? name, string source)
        {
            using var json = JsonDocument.Parse(source);
            return new(id, name, json.RootElement.Clone(), 0);
        }

        private static RawCoreFunctionImportSignature Signature(
            RawCanonicalImportIdentity identity,
            CanonicalAbiCoreSignature signature,
            WasmTarget target) => new(
                identity,
                [.. signature.Parameters.Select(value => Project(value, target))],
                signature.Result == CliValueKind.Void ? [] : [Project(signature.Result, target)]);

        private static RawCoreValueType Project(CliValueKind value, WasmTarget target) => value switch
        {
            CliValueKind.I4 or CliValueKind.ValueType => RawCoreValueType.I32,
            CliValueKind.I8 => RawCoreValueType.I64,
            CliValueKind.F4 => RawCoreValueType.F32,
            CliValueKind.F8 => RawCoreValueType.F64,
            CliValueKind.NativeInt or CliValueKind.ManagedReference or CliValueKind.ManagedAddress =>
                target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32,
            _ => throw new InvalidOperationException(),
        };
    }
}
