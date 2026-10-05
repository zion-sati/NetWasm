using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeMaterializationCacheKeyBuilderTests
{
    private readonly RuntimeMaterializationCacheKeyBuilder _builder = new();

    [Fact]
    public void NativeKeyTracksCurrentArchiveBytesPhysicalAbiAndEffectiveRequest()
    {
        var runtime = Request("/test");
        var plan = new RuntimeMemoryPlanBuilder().Build(new(runtime.Target, runtime.Manifest.WasmPageSize, 65_537, null, null));
        var binding = new RuntimeNativeBinding(new("mule", "sum", [RuntimeNativeValueType.I32], RuntimeNativeValueType.I32),
            new("mule", "wasm32", "/native/libmule.a", RuntimePackTestData.Digest));
        var baseline = new RuntimeNativeMaterializationCacheKeyRequest(runtime.BuildIdentity, runtime.Manifest, runtime.Target,
            plan, [binding], runtime.Optimization, runtime.LinkArguments, runtime.OptimizationArguments, runtime.AssetRoot, runtime.OutputPath);
        var key = _builder.Build(baseline);
        var profile = baseline.Target.NativeValidation!;
        var contract = profile.Imports[0];
        var changes = new[]
        {
            baseline with { Target = baseline.Target with { GarbageCollector = "Compact" } },
            baseline with { Plan = plan with { InitialHeapSizeBytes = plan.InitialHeapSizeBytes + 1 } },
            baseline with { Plan = plan with { MaximumMemorySizeBytes = plan.MaximumMemorySizeBytes - 65_536 } },
            baseline with { Bindings = [binding with { Provider = binding.Provider with { Sha256 = new string('b', 64) } }] },
            baseline with { Bindings = [binding with { Provider = binding.Provider with { Path = "/native/other.a" } }] },
            baseline with { Bindings = [binding with { Import = binding.Import with { EntryPoint = "other" } }] },
            baseline with { Bindings = [binding with { Import = binding.Import with { LibraryName = "other" } }] },
            baseline with { Bindings = [binding with { Import = binding.Import with { Parameters = [RuntimeNativeValueType.I64] } }] },
            baseline with { Bindings = [binding with { Import = binding.Import with { ReturnType = null } }] },
            baseline with { BuildIdentity = baseline.BuildIdentity with { WasmLdVersion = "lld-2" } },
            baseline with { LinkArguments = baseline.LinkArguments.Add("--new-policy") },
            baseline with { Target = baseline.Target with { NativeValidation = profile with { Version = 2 } } },
            baseline with { Target = baseline.Target with { NativeValidation = profile with { Features = profile.Features.Add("other") } } },
            baseline with { Target = baseline.Target with { NativeValidation = profile with { Imports = profile.Imports.Add(contract with { Name = "other" }) } } },
            baseline with { Target = baseline.Target with { NativeValidation = profile with { Imports = [contract with { Module = "other" }] } } },
            baseline with { Target = baseline.Target with { NativeValidation = profile with { Imports = [contract with { Parameters = [0x7e] }] } } },
            baseline with { Target = baseline.Target with { NativeValidation = profile with { Imports = [contract with { Results = [0x7f] }] } } },
            baseline with { Target = baseline.Target with { NativeValidation = profile with { Imports = [contract with { Required = false }] } } },
        };
        Assert.All(changes, changed => Assert.NotEqual(key, _builder.Build(changed)));
        Assert.NotEqual(key, _builder.Build(runtime));
        Assert.Equal(key, _builder.Build(baseline));
        Assert.Throws<ArgumentNullException>(() => _builder.Build((RuntimeNativeMaterializationCacheKeyRequest)null!));
        Assert.Throws<ArgumentNullException>(() => _builder.Build(baseline with { Plan = null! }));
        Assert.Throws<ArgumentNullException>(() => _builder.Build(baseline with { Target = baseline.Target with { NativeValidation = null } }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build(baseline with { Bindings = [] }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build(baseline with
        {
            NativeCallbackObjectPath = "/input/application.callbacks.o",
        }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build(baseline with
        {
            NativeCallbackAllowedUndefinedPath = "/input/application.callbacks.allow-undefined",
        }));
    }

    [Fact]
    public void IgnoresMachinePathsAndUnalignedSourceLayoutChanges()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();

        var firstKey = _builder.Build(Request(first.Path));
        var secondKey = _builder.Build(Request(second.Path));

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void ResolvedCollectorIdentityChangesOrdinaryCacheKeyEvenWithIdenticalAssetBytes()
    {
        var request = Request("/test");
        var changed = request with { Target = request.Target with { GarbageCollector = "Compact" } };
        Assert.NotEqual(_builder.Build(request), _builder.Build(changed));
        Assert.Equal(_builder.Build(changed), _builder.Build(changed));
    }

    [Fact]
    public void CallbackOnlyKeyBindsContentAndContractsButNotMachinePaths()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();
        var firstRequest = CallbackRequest(first.Path);
        var secondRequest = CallbackRequest(second.Path);

        var firstKey = _builder.Build(firstRequest);
        var support = firstRequest.NativeCallbackSupport!;
        var callback = Assert.Single(support.Callbacks);
        var secondCallback = callback with
        {
            NativeSymbol = callback.NativeSymbol + "_second",
            RuntimeImportSymbol = callback.RuntimeImportSymbol + "_second",
            ApplicationExportName = callback.ApplicationExportName + "_second",
            RuntimeGetterExportName = callback.RuntimeGetterExportName + "_second",
        };

        Assert.Equal(firstKey, _builder.Build(secondRequest));
        RuntimeNativeCallbackSupport[] changes =
        [
            support with { FileName = "other.callbacks.o" },
            support with { Sha256 = new string('b', 64) },
            support with { TemporaryApplicationExports = ["different"] },
            support with { TemporaryRuntimeExports = ["different"] },
            support with { Callbacks = [callback with { NativeSymbol = "different" }] },
            support with { Callbacks = [callback with { RuntimeImportSymbol = "different" }] },
            support with { Callbacks = [callback with { ApplicationExportName = "different" }] },
            support with { Callbacks = [callback with { RuntimeGetterExportName = null }] },
            support with { Callbacks = [callback with { RuntimeGetterExportName = "different" }] },
            support with { Callbacks = [callback with { Parameters = [RuntimeNativeValueType.I64, RuntimeNativeValueType.I32] }] },
            support with { Callbacks = [callback with { ReturnType = RuntimeNativeValueType.I64 }] },
            support with { Callbacks = [callback with { ReturnType = null }] },
            support with { Callbacks = [callback, secondCallback] },
        ];
        Assert.All(changes, changed => Assert.NotEqual(firstKey, _builder.Build(firstRequest with
        {
            NativeCallbackSupport = changed,
        })));
        Assert.NotEqual(
            _builder.Build(firstRequest with
            {
                NativeCallbackSupport = support with
                {
                    Callbacks = [callback, secondCallback],
                },
            }),
            _builder.Build(firstRequest with
            {
                NativeCallbackSupport = support with
                {
                    Callbacks = [secondCallback, callback],
                },
            }));
        Assert.NotEqual(
            _builder.Build(firstRequest with
            {
                NativeCallbackSupport = support with
                {
                    Callbacks = [callback with
                    {
                        Parameters = [RuntimeNativeValueType.I32, RuntimeNativeValueType.I64],
                    }],
                },
            }),
            _builder.Build(firstRequest with
            {
                NativeCallbackSupport = support with
                {
                    Callbacks = [callback with
                    {
                        Parameters = [RuntimeNativeValueType.I64, RuntimeNativeValueType.I32],
                    }],
                },
            }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build(firstRequest with
        {
            NativeCallbackObjectPath = null,
        }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build(firstRequest with
        {
            NativeCallbackAllowedUndefinedPath = null,
        }));
        Assert.Throws<InvalidOperationException>(() => _builder.Build(firstRequest with
        {
            Bindings = default,
        }));
    }

    [Fact]
    public void ChangesForEverySemanticInputClass()
    {
        using var directory = new TemporaryDirectory();
        var baseline = Request(directory.Path);
        var baselineKey = _builder.Build(baseline);
        var secondAsset = new RuntimePackAsset(
            "wasm32/system-libraries/libm.a",
            new string('b', 64));
        var orderedTarget = baseline.Target with
        {
            SystemLibraries = new(
                ["libc.a", "libm.a"],
                [baseline.Target.SystemLibraries.Assets[0], secondAsset]),
        };
        var reorderedTarget = orderedTarget with
        {
            SystemLibraries = new(
                ["libm.a", "libc.a"],
                [secondAsset, baseline.Target.SystemLibraries.Assets[0]]),
        };

        RuntimeMaterializationCacheKeyRequest[] changes =
        [
            baseline with { BuildIdentity = baseline.BuildIdentity with { CompilerVersion = "compiler-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { RuntimeVersion = "runtime-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { RuntimePackVersion = "runtime-pack-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { WasmLdVersion = "wasm-ld-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { WasmOptVersion = "wasm-opt-2" } },
            baseline with { BuildIdentity = baseline.BuildIdentity with { WasmToolsVersion = "wasm-tools-2" } },
            baseline with { Manifest = baseline.Manifest with { RuntimeAbi = "netwasm.runtime.v2" } },
            baseline with
            {
                Manifest = baseline.Manifest with
                {
                    Provenance = baseline.Manifest.Provenance with { ToolchainFingerprint = "toolchain-2" },
                },
            },
            baseline with { Layout = baseline.Layout with { RuntimeGlobalBase = 65_568 } },
            baseline with { Layout = baseline.Layout with { InitialMemorySizeBytes = 327_680 } },
            baseline with { Optimization = RuntimeWasmOptimization.O3 },
            baseline with
            {
                Target = baseline.Target with
                {
                    RuntimeArchive = baseline.Target.RuntimeArchive with { Sha256 = new string('c', 64) },
                },
            },
            baseline with { Target = orderedTarget },
            baseline with { Target = reorderedTarget },
            baseline with { LinkArguments = baseline.LinkArguments.Add("--new-link-policy") },
            baseline with { OptimizationArguments = baseline.OptimizationArguments.Add("--new-opt-policy") },
        ];

        Assert.All(changes, change => Assert.NotEqual(baselineKey, _builder.Build(change)));
        Assert.NotEqual(
            _builder.Build(baseline with { Target = orderedTarget }),
            _builder.Build(baseline with { Target = reorderedTarget }));
    }

    private static RuntimeMaterializationCacheKeyRequest Request(string root)
    {
        var assetRoot = Path.Combine(root, "runtime");
        var output = Path.Combine(root, "obj", "runtime.wasm");
        var target = RuntimePackTestData.Target("wasm32");
        return new(
            new(
                "sdk-1",
                "compiler-1",
                "runtime-1",
                "runtime-pack-1",
                "host-tools",
                "host-tools-1",
                "wasm-ld-1",
                "wasm-opt-1",
                "wasm-tools-1",
                "node-1"),
            RuntimePackTestData.Manifest(),
            target,
            RuntimePackTestData.Layout(),
            RuntimeWasmOptimization.Oz,
            ImmutableArray.Create(
                "-mwasm32",
                Path.Combine(assetRoot, target.RuntimeArchive.Path),
                $"--allow-undefined-file={Path.Combine(assetRoot, target.AllowedUndefinedSymbols.Path)}",
                "-o",
                output),
            ImmutableArray.Create("-Oz", output, "-o", output),
            assetRoot,
            output);
    }

    private static RuntimeNativeMaterializationCacheKeyRequest CallbackRequest(
        string root)
    {
        var runtime = Request(root);
        var plan = new RuntimeMemoryPlanBuilder().Build(new(
            runtime.Target,
            runtime.Manifest.WasmPageSize,
            65_537,
            null,
            null));
        var callbackObject = Path.Combine(root, "obj", "application.callbacks.o");
        var allowed = Path.Combine(root, "obj", "callbacks.allow-undefined");
        return new(
            runtime.BuildIdentity,
            runtime.Manifest,
            runtime.Target,
            plan,
            [],
            runtime.Optimization,
            runtime.LinkArguments
                .Insert(1, callbackObject)
                .Insert(2, $"--allow-undefined-file={allowed}"),
            runtime.OptimizationArguments,
            runtime.AssetRoot,
            runtime.OutputPath)
        {
            NativeCallbackSupport = RuntimePackTestData.CallbackSupport(),
            NativeCallbackObjectPath = callbackObject,
            NativeCallbackAllowedUndefinedPath = allowed,
        };
    }
}
