using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeLinkedImportValidatorTests
{
    private readonly ProfileStub _profiles = new();
    private IRuntimeLinkedImportValidator CreateValidator() => Assert.IsAssignableFrom<IRuntimeLinkedImportValidator>(
        new RuntimeLinkedImportValidator(_profiles));

    [Theory]
    [InlineData("wasm32", 0x7f)]
    [InlineData("wasm64", 0x7e)]
    public void RequiresExactDeclaredIdentitiesAndSignaturesWhileAllowingUnusedOptionalImports(string target, byte pointerType)
    {
        var validator = CreateValidator();
        var contract = Contract() with
        {
            Module = target == "wasm64" ? "cm64p2|wasi:cli/environment@0.2" : "cm32p2|wasi:cli/environment@0.2",
            Name = "get-environment",
            Parameters = [pointerType],
            Results = []
        };
        var optional = Contract() with { Name = "optional", Required = false };
        var profile = RuntimePackTestData.NativeProfile(target) with { Imports = [contract, optional] };
        var module = Module() with
        {
            FunctionTypes = [new(contract.Parameters, contract.Results)],
            MemoryLayout = Module().MemoryLayout with { Target = target },
            Imports = [new(contract.Module, contract.Name, 0, 0)]
        };

        validator.Validate(profile, module);
        validator.Validate(profile, module with
        {
            FunctionTypes = module.FunctionTypes.Add(new(optional.Parameters, optional.Results)),
            Imports = module.Imports.Add(new(optional.Module, optional.Name, 0, 1)),
        });
        Assert.Throws<InvalidOperationException>(() => validator.Validate(profile, module with
        { Imports = [new(target == "wasm64" ? "cm32p2|wasi:cli/environment@0.2" : "cm64p2|wasi:cli/environment@0.2", contract.Name, 0, 0)] }));
        Assert.Equal(3, _profiles.Requests.Count);
        Assert.All(_profiles.Requests, request => Assert.Equal((profile, target), request));
    }

    [Fact]
    public void RejectsUninitializedOrMalformedContracts()
    {
        var profile = Profile();
        var validator = CreateValidator();
        Assert.Throws<ArgumentNullException>(() => validator.Validate(profile, null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(profile, Module() with { MemoryLayout = null! }));
        Assert.Throws<ArgumentNullException>(() => new RuntimeLinkedImportValidator(null!));
        Assert.Empty(_profiles.Requests);
        Assert.Throws<InvalidOperationException>(() => validator.Validate(profile, Module() with { Imports = default }));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(profile, Module() with { FunctionTypes = default }));
        _profiles.Failure = new InvalidOperationException("invalid profile");
        Assert.Same(_profiles.Failure, Assert.Throws<InvalidOperationException>(() => validator.Validate(profile, Module())));
    }

    [Fact]
    public void RejectsUnknownDuplicatedNonFunctionOrMissingImportsAndInvalidPhysicalSignatures()
    {
        var profile = Profile();
        var validator = CreateValidator();
        var module = Module();
        var import = module.Imports[0];
        foreach (var invalid in new[]
        {
            module with { Imports = [null!] }, module with { Imports = [import with { Kind = 4 }] },
            module with { Imports = [import, import] }, module with { Imports = [import with { Name = "unknown" }] },
            module with { Imports = [import with { Module = "wasi_snapshot_preview1" }] },
            module with { Imports = [import with { Module = "wasi_unstable" }] },
            module with { Imports = [import with { Module = "arbitrary-host" }] },
            module with { Imports = [import with { TypeIndex = 1 }] }, module with { Imports = [] },
            module with { FunctionTypes = [null!] }, module with { FunctionTypes = [new(default, [])] },
            module with { FunctionTypes = [new([0x7f], default)] },
            module with { FunctionTypes = [new([0x7e], [])] }, module with { FunctionTypes = [new([0x7f], [0x7f])] },
        })
            Assert.Throws<InvalidOperationException>(() => validator.Validate(profile, invalid));
        validator.Validate(profile with { Imports = [Contract() with { Required = false }] }, module with { Imports = [] });
    }

    [Theory]
    [InlineData("wasm32", 0x7f)]
    [InlineData("wasm64", 0x7e)]
    public void AdmitsOnlyExactCompilerDeclaredCallbackImports(
        string target,
        byte pointerType)
    {
        var profile = RuntimePackTestData.NativeProfile(target);
        var support = RuntimePackTestData.CallbackSupport(target);
        var runtime = profile.Imports[0];
        var module = Module() with
        {
            MemoryLayout = Module().MemoryLayout with { Target = target },
            FunctionTypes =
            [
                new(runtime.Parameters, runtime.Results),
                new([0x7f, pointerType], [0x7f]),
            ],
            Imports =
            [
                new(runtime.Module, runtime.Name, 0, 0),
                new("netwasm.application.v1",
                    "__netwasm_application_callback_0", 0, 1),
            ],
        };
        var validator = CreateValidator();

        validator.Validate(profile, module, support);

        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            profile,
            module with { Imports = module.Imports.RemoveAt(1) },
            support));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            profile,
            module with
            {
                Imports = module.Imports.SetItem(1,
                    module.Imports[1] with { Name = "unplanned" }),
            },
            support));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            profile,
            module with
            {
                FunctionTypes = module.FunctionTypes.SetItem(1,
                    new([pointerType], [0x7f])),
            },
            support));

        var optionalSupport = support with
        {
            Callbacks =
            [
                support.Callbacks[0] with
                {
                    RuntimeGetterExportName = null,
                    ReturnType = null,
                },
            ],
            TemporaryRuntimeExports = [],
        };
        validator.Validate(
            profile,
            module with { Imports = module.Imports.RemoveAt(1) },
            optionalSupport);
    }

    [Fact]
    public void RejectsDuplicateCallbackIdentityAndUnsupportedPhysicalTypes()
    {
        var profile = Profile();
        var support = RuntimePackTestData.CallbackSupport();
        var callback = support.Callbacks[0];
        var validator = CreateValidator();
        var duplicate = new RuntimeNativeImportContract(
            "netwasm.application.v1",
            callback.ApplicationExportName,
            [],
            [],
            false);

        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            profile with { Imports = profile.Imports.Add(duplicate) },
            Module(),
            support));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            profile,
            Module(),
            support with
            {
                Callbacks =
                [
                    callback with
                    {
                        Parameters = [(RuntimeNativeValueType)byte.MaxValue],
                    },
                ],
            }));
    }

    [Fact]
    public void ValidatesFloatingPointCallbackPhysicalTypes()
    {
        var profile = Profile();
        var support = RuntimePackTestData.CallbackSupport();
        var callback = support.Callbacks[0] with
        {
            Parameters =
            [
                RuntimeNativeValueType.F32,
                RuntimeNativeValueType.F64,
            ],
            ReturnType = RuntimeNativeValueType.F64,
        };
        support = support with { Callbacks = [callback] };
        var module = Module() with
        {
            FunctionTypes = Module().FunctionTypes.Add(
                new([0x7d, 0x7c], [0x7c])),
            Imports = Module().Imports.Add(new(
                "netwasm.application.v1",
                callback.ApplicationExportName,
                0,
                1)),
        };

        CreateValidator().Validate(profile, module, support);
    }

    private static RuntimeNativeImportContract Contract() => new("env", "emscripten_notify_memory_growth", [0x7f], [], true);
    private static RuntimeNativeValidationProfile Profile() => RuntimePackTestData.NativeProfile();
    private static RuntimeLinkedModule Module() => new(new("wasm32", 0, 0, 0, 0, 0, 65_536, 65_536),
        [new([0x7f], [])], [0], [], [])
    { Imports = [new("env", "emscripten_notify_memory_growth", 0, 0)] };

    private sealed class ProfileStub : IRuntimeNativeValidationProfileValidator
    {
        public List<(RuntimeNativeValidationProfile Profile, string Target)> Requests { get; } = [];
        public Exception? Failure { get; set; }
        public void Validate(RuntimeNativeValidationProfile profile, string target)
        {
            Requests.Add((profile, target));
            if (Failure is not null) throw Failure;
        }
    }
}
