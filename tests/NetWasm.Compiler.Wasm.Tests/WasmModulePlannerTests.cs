using System.Collections.Immutable;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmModulePlannerTests
{
    [Fact]
    public void PlanAllocatesImportsConstructedMethodsAndDelegateHelpersInOrder()
    {
        var program = new PlannerProgram();
        var delegateType = program.DelegateType;
        var invoke = new MethodInstanceModel(
            program.Invoke,
            delegateType,
            [],
            program.Invoke.Signature);
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [ConstructorKey] = MethodWithVirtualCalls(
                program,
                program.GetMethod(ConstructorKey),
                invoke),
            [EntryKey] = MethodWithVirtualCalls(program, program.Entry, invoke),
        };
        var constructedIdentity = new ManagedMethodIdentityFactory().Create(invoke).CanonicalName;
        var constructed = new Dictionary<string, StructuredMethod>
        {
            ["[Test]Test.Type<i4>::0x06000030"] = methods[EntryKey],
        };
        var jsImport = program.ImportMethod with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        };
        var witImport = program.WitImportMethod with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        };
        var equivalentWitImport = witImport with
        {
            Key = Key(0x0600003f),
            Name = "EquivalentWitImport",
        };
        var constructedMethod = constructed.Values.Single();
        constructed.Clear();
        constructed.Add(constructedIdentity, constructedMethod);

        var request = WasmEmissionRequest.Create(
            program.Entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            constructedMethods: constructed,
            methodInstances: ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(constructedIdentity, invoke),
            delegateTypes: [delegateType, program.SecondDelegateType],
            jsImportMethods: [jsImport],
            witImportMethods: [witImport, equivalentWitImport],
            hostCallbacks:
            [
                new HostCallbackDeclaration(EntryKey, 0, invoke, "invoke"),
                new HostCallbackDeclaration(
                    EntryKey,
                    1,
                    program.SecondInvokeInstance,
                    "second"),
            ],
            callableMethods: CreateCallableMethods(program, methods.Keys));

        var plan = BuildThroughContract(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            request);
        var expectedMethodEmissions = new StructuredMethodEmissionPlanner(
            new ManagedMethodIdentityFactory(),
            new TestCilTypeIdentityResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StructuredMethodEmissionPlanner>.Instance).Plan(request);
        var expectedConstructedIdentities = expectedMethodEmissions
            .Where(method => request.ConstructedMethods.ContainsKey(method.Identity.CanonicalName))
            .Select(method => method.Identity);

        Assert.True(plan.OrderedMethods
            .Select(method => method.MethodKey)
            .SequenceEqual([EntryKey, ConstructorKey]));
        Assert.True(plan.OrderedConstructedMethods.SequenceEqual(expectedConstructedIdentities));
        Assert.Equal(2, plan.DelegateInvokes.Length);
        Assert.True(plan.DelegateInvokes
            .Select(method => method.DeclaringType.CanonicalName)
            .SequenceEqual([
                delegateType.CanonicalName,
                program.SecondDelegateType.CanonicalName,
            ]));
        Assert.True(plan.FunctionIndices.TryGetMethod(jsImport.Key, out var jsIndex));
        Assert.True(plan.FunctionIndices.TryGetMethod(witImport.Key, out var witIndex));
        Assert.True(plan.FunctionIndices.TryGetMethod(
            equivalentWitImport.Key,
            out var equivalentWitIndex));
        Assert.Equal(plan.RuntimeImports.Length, jsIndex.Value);
        Assert.Equal(jsIndex.Value + 1, witIndex.Value);
        Assert.Equal(witIndex, equivalentWitIndex);
        Assert.True(plan.DelegateCountHelperIndex.IsPresent);
        Assert.True(plan.DelegateLeafHelperIndex.IsPresent);
        Assert.True(plan.DelegateEqualityHelperIndex.IsPresent);
        Assert.True(plan.DelegateRemoveHelperIndex.IsPresent);
    }

    [Fact]
    public void InvalidVirtualOperandFailsBeforePlanValidation()
    {
        var program = new PlannerProgram();
        var body = new CilMethodBody(
            program.Entry,
            2,
            [],
            [
                I(0, CilOperation.CallVirtual, new CilOperand.None()),
                I(1, CilOperation.Return),
            ]);
        var method = UnvalidatedMethod(body);
        var request = WasmEmissionRequest.Create(
            program.Entry,
            new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = method },
            EmptyRootMaps([EntryKey]),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            callableMethods: CreateCallableMethods(program, [EntryKey]));

        var exception = Assert.Throws<InvalidOperationException>(() => BuildThroughContract(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            request));

        Assert.Contains("has no method operand", exception.Message);
    }

    [Fact]
    public void PlanOrdersMethodsAndAllocatesIndicesAfterRuntimeImports()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EntryKey);
        var constructor = program.GetMethod(ConstructorKey);
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [ConstructorKey] = Structure(program, constructor, I(0, CilOperation.Return)),
            [EntryKey] = Structure(
                program,
                entry,
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(1, CilOperation.Return)),
        };
        var request = WasmEmissionRequest.Create(
            entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            callableMethods: CreateCallableMethods(program, methods.Keys));

        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var plan = BuildThroughContract(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                runtimeImports,
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            request);

        Assert.True(plan.OrderedMethods
            .Select(method => method.MethodKey)
            .SequenceEqual([EntryKey, ConstructorKey]));
        Assert.True(plan.FunctionIndices.TryGetMethod(EntryKey, out var entryIndex));
        Assert.True(plan.FunctionIndices.TryGetMethod(
            ConstructorKey, out var constructorIndex));
        Assert.Equal(runtimeImports.Resolve(WasmModuleProfile.CoreApplication).Length, entryIndex.Value);
        Assert.Equal(entryIndex.Value + 1, constructorIndex.Value);
        Assert.False(plan.InteropImports.StringLength.IsPresent);
        Assert.False(plan.DelegateCountHelperIndex.IsPresent);
    }

    [Fact]
    public void DuplicateManagedDefinitionFailsBeforeFunctionIndexAllocation()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EntryKey);
        var structured = Structure(
            program,
            entry,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [EntryKey] = structured,
        };
        var request = WasmEmissionRequest.Create(
            entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            callableMethods: CreateCallableMethods(program, methods.Keys));
        var identities = new ManagedMethodIdentityFactory();
        var identity = identities.Create(
            entry,
            new TestCilTypeIdentityResolver().Resolve(entry.DeclaringType));
        var methodEmissions = ImmutableArray.Create(
            new StructuredMethodEmission(identity, structured),
            new StructuredMethodEmission(
                new ManagedMethodIdentity($"{identity.CanonicalName}:duplicate"),
                structured));
        var planner = new WasmModulePlanner(
            program,
            program,
            program,
            new InteropImportPlanner(),
            WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
            new WasmModulePlanInvariantValidator());

        var exception = Assert.Throws<CompilerException>(
            () => ((IWasmModulePlanner)planner).Build(request, methodEmissions));

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains(
            "DUPLICATE_MANAGED_DEFINITION",
            exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentPlanIncludesTerminalReporterWhenItExportsHostCallbacks()
    {
        var program = new PlannerProgram();
        var invoke = new MethodInstanceModel(
            program.Invoke,
            program.DelegateType,
            [],
            program.Invoke.Signature);
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [EntryKey] = MethodWithVirtualCalls(program, program.Entry, invoke),
        };
        var componentContract = new ComponentBoundaryContract(
            "example:test@1.0.0",
            "test",
            [],
            [new CanonicalAbiFunction(
                "example:test/api@1.0.0",
                "run",
                EntryKey,
                [],
                null)]);
        var request = WasmEmissionRequest.Create(
            program.Entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            hostCallbacks:
            [new HostCallbackDeclaration(EntryKey, 0, invoke, "callback")],
            componentContract: componentContract,
            moduleProfile: WasmModuleProfile.ComponentCoreModule,
            entryPointProfile: WasmEntryPointProfile.Internal,
            callableMethods: CreateCallableMethods(program, methods.Keys));
        var imports = WasmRuntimeImports.CreateCatalog();

        var plan = BuildThroughContract(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                imports,
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            request);

        Assert.True(plan.RuntimeImportSelection.IncludeTerminalExceptionReporter);
        var reporterIndex = imports.Resolve(
            RuntimeImportSymbol.ManagedTerminalExceptionReport,
            plan.RuntimeImportSelection);
        Assert.Equal(RuntimeAbi.RuntimeReportTerminalException,
            plan.RuntimeImports[reporterIndex].Name);
    }

    [Theory]
    [InlineData(WasmEntryPointProfile.Internal, false)]
    [InlineData(WasmEntryPointProfile.Process, true)]
    public void ComponentPlanSelectsTerminalReporterFromIndependentEntryPointProfile(
        WasmEntryPointProfile entryPointProfile,
        bool expectedReporter)
    {
        var program = new PlannerProgram();
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [EntryKey] = UnvalidatedMethod(new CilMethodBody(
                program.Entry,
                1,
                [],
                [
                    I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                    I(1, CilOperation.Return),
                ])),
        };
        var componentContract = new ComponentBoundaryContract(
            "example:test@1.0.0",
            "test",
            [],
            [new CanonicalAbiFunction(
                "example:test/api@1.0.0",
                "run",
                EntryKey,
                [],
                null)]);
        var request = WasmEmissionRequest.Create(
            program.Entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            componentContract: componentContract,
            moduleProfile: WasmModuleProfile.ComponentCoreModule,
            entryPointProfile: entryPointProfile,
            callableMethods: CreateCallableMethods(program, methods.Keys));
        var imports = WasmRuntimeImports.CreateCatalog();

        var plan = BuildThroughContract(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                imports,
                new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            request);

        Assert.Equal(
            expectedReporter,
            plan.RuntimeImportSelection.IncludeTerminalExceptionReporter);
        Assert.Equal(
            expectedReporter,
            plan.RuntimeImports.Any(import =>
                import.Name == RuntimeAbi.RuntimeReportTerminalException));
    }

    [Fact]
    public void ModuleTargetPreservesExplicitDelegateTypesForInstructionEmission()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EntryKey);
        var methods = new Dictionary<EntityKey, StructuredMethod>
        {
            [EntryKey] = Structure(
                program,
                entry,
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(1, CilOperation.Return)),
        };
        var delegateType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Callback",
            isValueType: false);
        var request = WasmEmissionRequest.Create(
            entry,
            methods,
            EmptyRootMaps(methods.Keys),
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            delegateTypes: [delegateType],
            callableMethods: CreateCallableMethods(program, methods.Keys));
        var layouts = new RecordingLayoutProvider();
        var targetFactory = new WasmModuleTargetFactory(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            new ModuleDataPlanner(
                layouts,
                layouts,
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            new TestStructuredMethodEmissionPlanner(
                new ManagedMethodIdentityFactory(),
                new TestCilTypeIdentityResolver()),
            new StaticInitializerFunctionPlanner(layouts, program));

        var target = targetFactory.Create(request);

        var forwarded = Assert.Single(target.Instructions.DelegateTypes);
        Assert.Equal(
            delegateType.CanonicalName,
            forwarded.CanonicalName);
    }

    private delegate WasmModulePlan WasmModulePlannerCall(
        IWasmModulePlanner planner,
        WasmEmissionRequest request);

    private static readonly WasmModulePlannerCall BuildThroughContract =
        static (planner, request) =>
        {
            var methodEmissions = new StructuredMethodEmissionPlanner(
            new ManagedMethodIdentityFactory(),
            new TestCilTypeIdentityResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StructuredMethodEmissionPlanner>.Instance).Plan(request);
            return planner.Build(request, methodEmissions);
        };

    private static StructuredMethod MethodWithVirtualCalls(
        PlannerProgram program,
        MethodDefinitionModel method,
        MethodInstanceModel invoke)
    {
        var body = new CilMethodBody(
            method,
            6,
            [],
            [
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.CallVirtual, new CilOperand.MethodInstance(invoke)),
                I(2, CilOperation.CallVirtual, new CilOperand.Entity(program.Invoke.Key)),
                I(3, CilOperation.CallVirtual,
                    new CilOperand.MethodInstance(program.SecondInvokeInstance)),
                I(4, CilOperation.CallVirtual,
                    new CilOperand.MethodInstance(program.NonInvokeInstance)),
                I(5, CilOperation.Return),
            ]);
        return UnvalidatedMethod(body);
    }

    private static StructuredMethod UnvalidatedMethod(CilMethodBody body)
        => new(
            new StructuredMethodHeader(
                body.Method,
                body.MethodInstance,
                body.MaxStack,
                body.Locals,
                body.LocalSignatureTypes,
                body.Instructions),
            new StructuredBlockId(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty,
            StructuredSequence.Empty,
            [],
            ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

    private static ImmutableDictionary<string, MethodInstanceModel> CreateCallableMethods<TProgram>(
        TProgram program,
        IEnumerable<EntityKey> methodKeys)
        where TProgram : IMethodRepository
    {
        var identities = new ManagedMethodIdentityFactory();
        return methodKeys
            .Select(program.GetMethod)
            .Select(method => new MethodInstanceModel(
                method,
                new TestCilTypeIdentityResolver().Resolve(method.DeclaringType),
                [],
                method.Signature))
            .ToImmutableDictionary(method => identities.Create(method).CanonicalName);
    }

    private sealed class PlannerProgram :
        ITypeRepository,
        IMethodRepository,
        ITypeClassifier,
        ISymbolFormatter
    {
        public static readonly EntityKey DelegateTypeKey = Key(0x02000020);
        public static readonly EntityKey InvokeKey = Key(0x06000030);
        public static readonly EntityKey ImportKey = Key(0x06000031);
        public static readonly EntityKey WitImportKey = Key(0x06000032);
        public static readonly EntityKey SecondDelegateTypeKey = Key(0x02000021);
        public static readonly EntityKey SecondInvokeKey = Key(0x06000033);
        public static readonly EntityKey NonInvokeKey = Key(0x06000034);

        private readonly FakeProgram _program = new();

        public PlannerProgram()
        {
            Entry = _program.GetMethod(EntryKey);
            Invoke = new(
                InvokeKey,
                DelegateTypeKey,
                "Invoke",
                false,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            SecondInvoke = new(
                SecondInvokeKey,
                SecondDelegateTypeKey,
                "Invoke",
                false,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            NonInvoke = new(
                NonInvokeKey,
                TypeKey,
                "Call",
                false,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            ImportMethod = new(
                ImportKey,
                TypeKey,
                "Import",
                true,
                MethodSignatureModel.Create(CliValueKind.I4),
                1);
            WitImportMethod = new(
                WitImportKey,
                TypeKey,
                "WitImport",
                true,
                MethodSignatureModel.Create(CliValueKind.I4),
                1)
            {
                WitImport = new("example:host@1.0.0/api", "read"),
            };
            DelegateType = CliTypeIdentity.Named(
                Assembly,
                "Test",
                "Delegate",
                isValueType: false);
            SecondDelegateType = CliTypeIdentity.Named(
                Assembly,
                "Test",
                "SecondDelegate",
                isValueType: false);
            SecondInvokeInstance = new(
                SecondInvoke,
                SecondDelegateType,
                [],
                SecondInvoke.Signature);
            NonInvokeInstance = new(
                NonInvoke,
                CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
                [],
                NonInvoke.Signature);
        }

        public MethodDefinitionModel Entry { get; }
        public MethodDefinitionModel Invoke { get; }
        public MethodDefinitionModel SecondInvoke { get; }
        public MethodDefinitionModel NonInvoke { get; }
        public MethodDefinitionModel ImportMethod { get; }
        public MethodDefinitionModel WitImportMethod { get; }
        public CliTypeIdentity DelegateType { get; }
        public CliTypeIdentity SecondDelegateType { get; }
        public MethodInstanceModel SecondInvokeInstance { get; }
        public MethodInstanceModel NonInvokeInstance { get; }

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            key == DelegateTypeKey
                ? new(key, "Test", "Delegate", false, [], [InvokeKey])
                : key == SecondDelegateTypeKey
                    ? new(key, "Test", "SecondDelegate", false, [], [SecondInvokeKey])
                : _program.GetTypeDefinition(key);

        public MethodDefinitionModel GetMethod(EntityKey key)
        {
            if (key == InvokeKey)
            {
                return Invoke;
            }
            if (key == SecondInvokeKey)
            {
                return SecondInvoke;
            }
            if (key == NonInvokeKey)
            {
                return NonInvoke;
            }
            if (key == ImportKey)
            {
                return ImportMethod;
            }
            if (key == WitImportKey)
            {
                return WitImportMethod;
            }
            return _program.GetMethod(key);
        }

        public bool IsDelegateType(EntityKey type) =>
            type == DelegateTypeKey || type == SecondDelegateTypeKey;
        public string Format(EntityKey key) => GetTypeDefinition(key).FullName;
        public string Format(MethodDefinitionModel method) =>
            $"{GetTypeDefinition(method.DeclaringType).FullName}::{method.Name}";
    }
}
