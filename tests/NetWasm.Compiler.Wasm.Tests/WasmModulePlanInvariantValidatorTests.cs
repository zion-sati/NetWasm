using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmModulePlanInvariantValidatorTests
{
    [Fact]
    public void RequiresCanonicalOrderingAcrossMultipleReachedNativeMethods()
    {
        var (request, plan) = CreateNativePlan();
        var first = Assert.Single(plan.NativeImports.Methods);
        var secondMethod = first.Method with
        {
            Definition = first.Method.Definition with { Key = Key(0x0600007e), Name = "Another" },
        };
        var ordered = new[] { first, first with { Method = secondMethod } }
            .OrderBy(import => import.Method.CanonicalName, StringComparer.Ordinal)
            .ToImmutableArray();
        request = request with
        {
            MethodInstances = request.MethodInstances.ToImmutableDictionary().Add(secondMethod.CanonicalName, secondMethod),
        };
        plan = plan with
        {
            NativeImports = new(ordered),
            FunctionIndices = plan.FunctionIndices with
            {
                ImportedMethods = ordered.Select((import, index) => (import.Method.Definition.Key, index))
                    .ToImmutableDictionary(pair => pair.Key, pair => new WasmFunctionIndex(pair.index)),
            },
        };
        var validator = CreateValidator();

        validator.Validate(request, [], plan);
        var error = Assert.Throws<InvalidOperationException>(() => validator.Validate(request, [],
            plan with { NativeImports = new([.. ordered.Reverse()]) }));

        Assert.Contains("canonical order", error.Message);
    }

    [Fact]
    public void AcceptsExactlyReachedNativeImportsAndTheirSequentialIndices()
    {
        var (request, plan) = CreateNativePlan();

        CreateValidator().Validate(request, [], plan);

        var native = Assert.Single(plan.NativeImports.Methods);
        Assert.Same(Assert.Single(request.MethodInstances.Values), native.Method);
        Assert.Equal(0, plan.FunctionIndices.ImportedMethods[native.Method.Definition.Key].Value);
    }

    [Fact]
    public void RejectsOmittedOrContradictoryReachedNativeImports()
    {
        var (request, plan) = CreateNativePlan();
        var validator = CreateValidator();
        var omitted = Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(request, [], plan with { NativeImports = NativeImportPlan.Empty }));
        Assert.Contains("native imports do not match reached native methods", omitted.Message);

        var native = Assert.Single(plan.NativeImports.Methods);
        var changed = native.Method with
        {
            Definition = native.Method.Definition with { Name = "different" },
        };
        var contradictory = Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(request, [], plan with
            {
                NativeImports = new([native with { Method = changed }]),
            }));
        Assert.Contains("native imports do not match reached native methods", contradictory.Message);
    }

    [Fact]
    public void RejectsNativeImportWithoutAnIndex()
    {
        var (request, plan) = CreateNativePlan();
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                ImportedMethods = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty,
            },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(request, [], invalid));

        Assert.Contains("native import has no function index", error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void RejectsNegativeOrNonSequentialNativeIndices(int index)
    {
        var (request, plan) = CreateNativePlan();
        var native = Assert.Single(plan.NativeImports.Methods);
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                ImportedMethods = plan.FunctionIndices.ImportedMethods.SetItem(
                    native.Method.Definition.Key, new(index)),
            },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(request, [], invalid));

        Assert.Contains("native import function index", error.Message);
    }

    [Fact]
    public void RejectsCallbackPlanThatContradictsReachability()
    {
        var program = new FakeProgram();
        var request = CreateRequest(program);
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var definition = program.GetMethod(EntryKey) with
        {
            Key = Key(0x0600007d),
            Name = "Callback",
            Signature = signature,
            NativeCallback = new([], null, false, false),
        };
        var callback = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Callbacks", false),
            [],
            signature);
        var namedDefinition = definition with
        {
            Key = Key(0x0600007c),
            Name = "NamedCallback",
            NativeCallback = new([], "named_callback", false, false),
        };
        var namedCallback = new MethodInstanceModel(
            namedDefinition,
            callback.DeclaringType,
            [],
            signature);
        request = request with
        {
            NativeCallbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(callback.CanonicalName, callback)
                .Add(namedCallback.CanonicalName, namedCallback),
            AddressedNativeCallbacks = ImmutableHashSet.Create(
                StringComparer.Ordinal,
                callback.CanonicalName),
        };
        var plan = CreatePlanner(program).Build(
            request,
            CreateMethodEmissions(request));
        var validator = CreateValidator();

        Validate(validator, request, plan);
        var missing = Assert.Throws<InvalidOperationException>(() =>
            Validate(validator, request, plan with
            {
                NativeCallbacks = NativeCallbackPlan.Empty,
            }));
        Assert.Contains("native callbacks do not match", missing.Message);

        var ownership = Assert.Throws<InvalidOperationException>(() =>
            Validate(
                validator,
                request with
                {
                    AddressedNativeCallbacks = ImmutableHashSet<string>.Empty,
                },
                plan));
        Assert.Contains("callback address ownership", ownership.Message);
    }

    [Fact]
    public void RejectsAnInvalidFunctionIndexBeforeSerialization()
    {
        var (request, plan) = CreatePlan();
        var indices = plan.FunctionIndices.DirectMethods.SetItem(
            EntryKey,
            new WasmFunctionIndex(-1));
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                DirectMethods = indices,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(CreateValidator(), request, invalid));

        Assert.Contains("before Wasm serialization", exception.Message);
        Assert.Contains("function index -1", exception.Message);
    }

    [Fact]
    public void ValidatesArguments()
    {
        var (request, plan) = CreatePlan();
        var validator = CreateValidator();
        var methodEmissions = CreateMethodEmissions(request);

        validator.Validate(request, methodEmissions, plan);
        Assert.Throws<ArgumentNullException>(
            () => validator.Validate(null!, methodEmissions, plan));
        Assert.Throws<ArgumentNullException>(
            () => validator.Validate(request, methodEmissions, null!));
    }

    [Fact]
    public void RejectsNonCanonicalDirectMethodOrder()
    {
        var (request, plan) = CreatePlan();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(
                CreateValidator(),
                request,
                plan with { OrderedMethods = [] }));

        Assert.Contains("order is not canonical", exception.Message);
    }

    [Fact]
    public void RejectsDirectMethodWithoutAnIndex()
    {
        var (request, plan) = CreatePlan();
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                DirectMethods = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(CreateValidator(), request, invalid));

        Assert.Contains("has no function index", exception.Message);
    }

    [Fact]
    public void RejectsUnplannedFunctionIndexMapEntry()
    {
        var (request, plan) = CreatePlan();
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                DirectMethods = plan.FunctionIndices.DirectMethods.Add(
                    Key(0x0600007f),
                    new WasmFunctionIndex(999)),
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(CreateValidator(), request, invalid));

        Assert.Contains("unplanned entry", exception.Message);
    }

    [Fact]
    public void AcceptsSequentialOptionalDelegateHelpersAndRejectsDuplicates()
    {
        var (request, plan) = CreatePlan();
        var firstHelper = plan.FunctionIndices.DirectMethods[EntryKey].Value + 1;
        var valid = plan with
        {
            DelegateCountHelperIndex = OptionalFunctionIndex.At(firstHelper),
            DelegateLeafHelperIndex = OptionalFunctionIndex.At(firstHelper + 1),
            DelegateEqualityHelperIndex = OptionalFunctionIndex.At(firstHelper + 2),
            DelegateRemoveHelperIndex = OptionalFunctionIndex.At(firstHelper + 3),
        };

        Validate(CreateValidator(), request, valid);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(
                CreateValidator(),
                request,
                valid with
                {
                    DelegateLeafHelperIndex = OptionalFunctionIndex.At(firstHelper),
                }));

        Assert.Contains("duplicated, or not the expected index", exception.Message);
    }

    [Fact]
    public void ValidatesImportsAndConstructedMethodsThroughContract()
    {
        var program = new FakeProgram();
        var request = CreateRequest(program) with
        {
            JSImportMethods = [program.GetMethod(ConstructorKey)],
            WitImportMethods =
            [
                program.GetMethod(StringLengthKey) with
                {
                    WitImport = new("example:host@1.0.0/api", "read"),
                },
            ],
            ConstructedMethods = new Dictionary<string, StructuredMethod>
            {
                ["zeta"] = CreateStructuredMethod(program),
                ["alpha"] = CreateStructuredMethod(program),
            },
        };
        var plan = CreatePlanner(program).Build(request, new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()).Plan(request));

        Validate(CreateValidator(), request, plan);
    }

    [Fact]
    public void RejectsImportWithoutFunctionIndex()
    {
        var program = new FakeProgram();
        var request = CreateRequest(program) with
        {
            JSImportMethods = [program.GetMethod(ConstructorKey)],
        };
        var plan = CreatePlanner(program).Build(request, new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()).Plan(request));
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                ImportedMethods = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(CreateValidator(), request, invalid));

        Assert.Contains("has no function index", exception.Message);
    }

    [Fact]
    public void RejectsWitImportWithoutFunctionIndex()
    {
        var program = new FakeProgram();
        var request = CreateRequest(program) with
        {
            WitImportMethods =
            [
                program.GetMethod(StringLengthKey) with
                {
                    WitImport = new("example:host@1.0.0/api", "read"),
                },
            ],
        };
        var plan = CreatePlanner(program).Build(
            request,
            new TestStructuredMethodEmissionPlanner(
                new ManagedMethodIdentityFactory(),
                new TestCilTypeIdentityResolver()).Plan(request));
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                ImportedMethods = plan.FunctionIndices.ImportedMethods.Remove(StringLengthKey),
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(CreateValidator(), request, invalid));

        Assert.Contains("has no function index", exception.Message);
    }

    [Fact]
    public void RejectsWitImportGroupWithDifferentFunctionIndices()
    {
        var program = new FakeProgram();
        var import = new WitImportDeclaration("example:host@1.0.0/api", "read");
        var request = CreateRequest(program) with
        {
            WitImportMethods =
            [
                program.GetMethod(ConstructorKey) with { WitImport = import },
                program.GetMethod(StringLengthKey) with { WitImport = import },
            ],
        };
        var plan = CreatePlanner(program).Build(
            request,
            new TestStructuredMethodEmissionPlanner(
                new ManagedMethodIdentityFactory(),
                new TestCilTypeIdentityResolver()).Plan(request));
        var groupIndex = plan.FunctionIndices.ImportedMethods[ConstructorKey].Value;
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                ImportedMethods = plan.FunctionIndices.ImportedMethods.SetItem(
                    StringLengthKey,
                    new WasmFunctionIndex(groupIndex + 1)),
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(CreateValidator(), request, invalid));

        Assert.Contains("does not share function index", exception.Message);
    }

    [Fact]
    public void RejectsNonCanonicalConstructedMethodOrder()
    {
        var program = new FakeProgram();
        var request = CreateRequest(program) with
        {
            ConstructedMethods = new Dictionary<string, StructuredMethod>
            {
                ["alpha"] = CreateStructuredMethod(program),
            },
        };
        var plan = CreatePlanner(program).Build(request, new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()).Plan(request));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(
                CreateValidator(),
                request,
                plan with { OrderedConstructedMethods = [] }));

        Assert.Contains("constructed method order is not canonical", exception.Message);
    }

    [Fact]
    public void RejectsConstructedMethodWithoutAnIndex()
    {
        var program = new FakeProgram();
        var request = CreateRequest(program) with
        {
            ConstructedMethods = new Dictionary<string, StructuredMethod>
            {
                ["alpha"] = CreateStructuredMethod(program),
            },
        };
        var plan = CreatePlanner(program).Build(request, new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()).Plan(request));
        var invalid = plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                ConstructedMethods = ImmutableDictionary<string, WasmFunctionIndex>.Empty,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(CreateValidator(), request, invalid));

        Assert.Contains("constructed method 'alpha' has no function index", exception.Message);
    }

    [Fact]
    public void ValidatesDelegateInvokeHelperThroughContract()
    {
        var program = new FakeProgram();
        var (request, plan) = CreatePlan();
        var method = program.GetMethod(EntryKey);
        var invoke = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Delegate", isValueType: false),
            [],
            method.Signature);
        var helperIndex = plan.RuntimeImports.Length + plan.InteropImports.Imports.Length +
            plan.OrderedMethods.Length + plan.OrderedConstructedMethods.Length;
        var valid = plan with
        {
            DelegateInvokes = [invoke],
            FunctionIndices = plan.FunctionIndices with
            {
                DelegateInvokeHelpers = ImmutableDictionary<string, WasmFunctionIndex>.Empty
                    .Add(invoke.DeclaringType.CanonicalName, new(helperIndex)),
            },
        };

        Validate(CreateValidator(), request, valid);
    }

    private static void Validate(
        IWasmModulePlanInvariantValidator validator,
        WasmEmissionRequest request,
        WasmModulePlan plan) =>
        validator.Validate(request, CreateMethodEmissions(request), plan);

    private static ImmutableArray<StructuredMethodEmission> CreateMethodEmissions(
        WasmEmissionRequest request) =>
        new TestStructuredMethodEmissionPlanner(
            new ManagedMethodIdentityFactory(),
            new TestCilTypeIdentityResolver()).Plan(request);

    private static IWasmModulePlanInvariantValidator CreateValidator() => new[]
    {
        new WasmModulePlanInvariantValidator(),
    }.Cast<IWasmModulePlanInvariantValidator>().Single();

    private static (WasmEmissionRequest Request, WasmModulePlan Plan) CreatePlan()
    {
        var program = new FakeProgram();
        var request = CreateRequest(program);
        return (request, CreatePlanner(program).Build(request, new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()).Plan(request)));
    }

    internal static (WasmEmissionRequest Request, WasmModulePlan Plan) CreateNativePlan()
    {
        var program = new FakeProgram();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.NativeInt);
        var definition = program.GetMethod(EntryKey) with
        {
            Key = Key(0x0600007f),
            Name = "Native",
            RelativeVirtualAddress = 0,
            Signature = signature,
            NativeImport = new("mule", "native", System.Reflection.MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        };
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "Test", "Native", false), [], signature);
        var request = WasmEmissionRequest.Create(program.GetMethod(EntryKey),
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty, EmptyRootMaps([]), [],
            ImmutableDictionary<string, EntityKey>.Empty,
            methodInstances: ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(method.CanonicalName, method));
        var native = new NativeMethodImport(method,
            NativeAbiTestSupport.Plan(definition.NativeImport!, signature, signature),
            new(RuntimeAbi.RuntimeModule, "native", new(signature.ParameterTypes, signature.ReturnType)));
        var plan = new WasmModulePlan(new(WasmModuleProfile.CoreApplication, false), [], [], [],
            StackTraceMethodPlan.Disabled, [],
            new(ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty,
                ImmutableDictionary<string, WasmFunctionIndex>.Empty,
                ImmutableDictionary<string, WasmFunctionIndex>.Empty,
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(definition.Key, new(0))),
            new([], OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing),
            OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing,
            OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing)
        {
            NativeImports = new([native]),
        };
        return (request, plan);
    }

    private static WasmEmissionRequest CreateRequest(FakeProgram program)
    {
        var entry = program.GetMethod(EntryKey);
        var instance = new MethodInstanceModel(
            entry,
            new TestCilTypeIdentityResolver().Resolve(entry.DeclaringType),
            [],
            entry.Signature);
        var identity = new ManagedMethodIdentityFactory().Create(instance);
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [EntryKey] = Structure(
                program,
                entry,
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(1, CilOperation.Return)),
        };
        return WasmEmissionRequest.Create(
            entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            callableMethods: ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(identity.CanonicalName, instance));
    }

    private static StructuredMethod CreateStructuredMethod(FakeProgram program) =>
        Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));

    private static WasmModulePlanner CreatePlanner(FakeProgram program) => new(
        program,
        program,
        program,
        new FakeIntrinsics(),
        new InteropImportPlanner(),
        WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
        new WasmModulePlanInvariantValidator(),
        new NativeImportPlanner(NativeAbiTestSupport.ScalarPlanner()),
        NativeAbiTestSupport.CallbackPlanner());
}
