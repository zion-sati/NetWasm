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
        new InteropImportPlanner(),
        WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
        new WasmModulePlanInvariantValidator());
}
