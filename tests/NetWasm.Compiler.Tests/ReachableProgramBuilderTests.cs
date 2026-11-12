using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Tests;

public sealed class ReachableProgramBuilderTests
{
    [Fact]
    public void BuilderMaterializesAnEmptyReachabilitySnapshot()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var entry = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 2),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var snapshot = ReachabilityLedgerSnapshot.From(
            new ReachabilityLedgerFactory().Create());
        var builder = new ReachableProgramBuilder(
            new EmptyAllocationAnalyzer(),
            new EmptyByRefEffectAnalyzer(),
            new EmptyDelegateBindingPlanner());

        var program = builder.Build(
            entry,
            [],
            snapshot,
            new NoDelegateTypeRecognizer(),
            new EmptyTypeTestPlanner());

        Assert.Same(entry, program.EntryPoint);
        Assert.Empty(program.Methods);
        Assert.Empty(program.DispatchCallSites);
        Assert.Empty(program.TypeTestSites);
        Assert.Empty(program.DelegateTypes);
        Assert.Empty(program.DelegateBindings);
        Assert.Empty(program.EnumMetadataRequirements);
        Assert.Empty(program.MethodDescriptors);
        Assert.Empty(program.FieldDescriptors);
    }

    [Fact]
    public void BuilderPublishesManagedByRefEffectAnalysis()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var entry = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 2),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var expected = new ManagedByRefEffects(
            ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty.Add(
                "method",
                new([0], [], [])));
        var analyzer = new FixedByRefEffectAnalyzer(expected);
        var builder = new ReachableProgramBuilder(
            new EmptyAllocationAnalyzer(),
            analyzer,
            new EmptyDelegateBindingPlanner());

        var program = builder.Build(
            entry,
            [],
            ReachabilityLedgerSnapshot.From(
                new ReachabilityLedgerFactory().Create()),
            new NoDelegateTypeRecognizer(),
            new EmptyTypeTestPlanner());

        Assert.Same(expected, program.ManagedByRefEffects);
        Assert.Equal(1, analyzer.Calls);
    }

    [Fact]
    public void BuilderPublishesTheFrozenMemberDescriptorSnapshot()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var typeKey = new EntityKey(assembly, 1);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Target",
            isValueType: false);
        var entry = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            typeKey,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var method = new MethodInstanceModel(entry, declaringType, [], entry.Signature);
        var fieldDefinition = new FieldDefinitionModel(
            new EntityKey(assembly, 3),
            typeKey,
            "Value",
            CliValueKind.I4,
            IsStatic: false);
        var field = new FieldInstanceModel(
            fieldDefinition,
            declaringType,
            fieldDefinition.SignatureType);
        var ledger = new ReachabilityLedgerFactory().Create();
        ledger.MethodDescriptors.Add(method.CanonicalName, method);
        ledger.FieldDescriptors.Add(field.CanonicalName, field);
        ledger.RequiresGenericArguments = true;
        var snapshot = ReachabilityLedgerSnapshot.From(ledger);
        ledger.MethodDescriptors.Clear();
        ledger.FieldDescriptors.Clear();
        ledger.RequiresGenericArguments = false;

        var program = new ReachableProgramBuilder(
            new EmptyAllocationAnalyzer(),
            new EmptyByRefEffectAnalyzer(),
            new EmptyDelegateBindingPlanner()).Build(
                entry,
                [],
                snapshot,
                new NoDelegateTypeRecognizer(),
                new EmptyTypeTestPlanner());

        Assert.Same(snapshot.MethodDescriptors, program.MethodDescriptors);
        Assert.Same(snapshot.FieldDescriptors, program.FieldDescriptors);
        Assert.True(program.RequiresGenericArguments);
        Assert.Same(method, Assert.Single(program.MethodDescriptors).Value);
        Assert.Same(field, Assert.Single(program.FieldDescriptors).Value);
    }

    [Fact]
    public void BuilderPreservesDeduplicatedEnumDemandAndSnapshotIsolation()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var entry = new MethodDefinitionModel(
            new EntityKey(assembly, 1), new EntityKey(assembly, 2), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var first = new EnumMetadataRequirement(new EntityKey(assembly, 3),
            EnumMetadataPayload.Values);
        var second = new EnumMetadataRequirement(new EntityKey(assembly, 3),
            EnumMetadataPayload.Names);
        var conservative = new EnumMetadataRequirement(null, EnumMetadataPayload.Names);
        var ledger = new ReachabilityLedgerFactory().Create();
        ledger.EnumMetadataRequirements.UnionWith([first, first, second, conservative]);
        var snapshot = ReachabilityLedgerSnapshot.From(ledger);
        ledger.EnumMetadataRequirements.Clear();
        var builder = new ReachableProgramBuilder(
            new EmptyAllocationAnalyzer(),
            new EmptyByRefEffectAnalyzer(),
            new EmptyDelegateBindingPlanner());

        var program = builder.Build(entry, [], snapshot,
            new NoDelegateTypeRecognizer(), new EmptyTypeTestPlanner());

        Assert.Equal(3, program.EnumMetadataRequirements.Count);
        Assert.Contains(first, program.EnumMetadataRequirements);
        Assert.Contains(second, program.EnumMetadataRequirements);
        Assert.Contains(conservative, program.EnumMetadataRequirements);
        Assert.Empty(ledger.EnumMetadataRequirements);
        Assert.Same(snapshot.EnumMetadataRequirements, program.EnumMetadataRequirements);
    }

    [Fact]
    public void BuilderOrdersJavaScriptImportsByHostModuleAndFunction()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var entry = new MethodDefinitionModel(
            new EntityKey(assembly, 3),
            new EntityKey(assembly, 4),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1)
        {
            JSImport = new InteropImportDeclaration("read", null),
        };
        var environmentImport = entry with
        {
            Key = new EntityKey(assembly, 5),
            JSImport = new InteropImportDeclaration("write", "env"),
        };
        var snapshot = ReachabilityLedgerSnapshot.From(
                new ReachabilityLedgerFactory().Create()) with
        {
            JavaScriptImports = ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty
                .Add(entry.Key, entry)
                .Add(environmentImport.Key, environmentImport),
        };
        var builder = new ReachableProgramBuilder(
            new EmptyAllocationAnalyzer(),
            new EmptyByRefEffectAnalyzer(),
            new EmptyDelegateBindingPlanner());

        var program = builder.Build(
            entry,
            [],
            snapshot,
            new NoDelegateTypeRecognizer(),
            new EmptyTypeTestPlanner());

        Assert.Equal(2, program.JSImportMethods.Length);
        Assert.Equal("write", program.JSImportMethods[0].JSImport!.FunctionName);
        Assert.Equal("read", program.JSImportMethods[1].JSImport!.FunctionName);
    }

    [Fact]
    public void BuilderPlansTypedBindingsForReachableDelegateInvokes()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var delegateTypeKey = new EntityKey(assembly, 20);
        var delegateType = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Callback",
            isValueType: false);
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.ManagedReference);
        var invokeDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 21),
            delegateTypeKey,
            "Invoke",
            false,
            signature,
            1);
        var targetDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 22),
            new EntityKey(assembly, 23),
            "Target",
            true,
            signature,
            1);
        var invoke = new MethodInstanceModel(
            invokeDefinition,
            delegateType,
            [],
            signature);
        var target = new MethodInstanceModel(
            targetDefinition,
            CliTypeIdentity.Named(
                assembly,
                "Tests",
                "TargetOwner",
                isValueType: false),
            [],
            signature);
        var binding = new ManagedDelegateBinding(
            new ManagedMethodIdentity(invoke.CanonicalName),
            new ManagedMethodIdentity(target.CanonicalName),
            invoke,
            target,
            [
                new ManagedDelegateValueBinding(
                    signature.ParameterSignatureTypes[0],
                    signature.ParameterSignatureTypes[0],
                    ManagedDelegateAdaptation.Identity),
            ],
            new ManagedDelegateValueBinding(
                signature.ReturnSignatureType,
                signature.ReturnSignatureType,
                ManagedDelegateAdaptation.Identity));
        var ledger = new ReachabilityLedgerFactory().Create();
        var callerIdentity = new ManagedMethodIdentity("caller");
        var callSiteKey = new ManagedCallSiteKey(callerIdentity, 4);
        ledger.ManagedCallSites.Add(
            callSiteKey,
            new ManagedCallSite(
                callSiteKey,
                ManagedCallOperation.Virtual,
                binding.InvokeIdentity,
                invoke,
                null));
        ledger.CallableMethods.Add(target.CanonicalName, target);
        ledger.AllocatedTypes.Add(delegateType);
        var planner = new RecordingDelegateBindingPlanner([binding]);
        var builder = new ReachableProgramBuilder(
            new EmptyAllocationAnalyzer(),
            new EmptyByRefEffectAnalyzer(),
            planner);

        var program = builder.Build(
            targetDefinition,
            [],
            ReachabilityLedgerSnapshot.From(ledger),
            new RecognizingDelegateType(delegateType),
            new EmptyTypeTestPlanner());

        Assert.Equal([invoke], planner.Invokes);
        Assert.Equal([target], planner.Targets);
        Assert.Same(binding, Assert.Single(program.DelegateBindings));
        Assert.Same(delegateType, Assert.Single(program.DelegateTypes));
    }

    // A delegate's Invoke called directly, or bound by a delegate converted from another
    // (ldftn, ldvirtftn), is planned as an invoke like a callvirt's: its type gets an invoke
    // helper with the bindings it dispatches to (issue 223).
    [Theory]
    [InlineData(ManagedCallOperation.Direct)]
    [InlineData(ManagedCallOperation.Virtual)]
    [InlineData(ManagedCallOperation.LoadFunction)]
    [InlineData(ManagedCallOperation.LoadVirtualFunction)]
    public void EveryCallOrAddressOfADelegatesInvokeIsPlannedAsAnInvoke(ManagedCallOperation operation)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var delegateType = CliTypeIdentity.Named(assembly, "Tests", "Transform", isValueType: false);
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var invokeDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 31), new EntityKey(assembly, 30), "Invoke", false, signature, 1);
        var invoke = new MethodInstanceModel(invokeDefinition, delegateType, [], signature);
        var ordinary = new MethodInstanceModel(invokeDefinition with { Name = "Run" }, delegateType, [], signature);
        var notADelegate = new MethodInstanceModel(invokeDefinition,
            CliTypeIdentity.Named(assembly, "Tests", "Invoker", isValueType: false), [], signature);
        var ledger = new ReachabilityLedgerFactory().Create();
        foreach (var (offset, target) in new[] { (4, invoke), (8, ordinary), (12, notADelegate) })
        {
            var key = new ManagedCallSiteKey(new ManagedMethodIdentity("caller"), offset);
            ledger.ManagedCallSites.Add(key,
                new ManagedCallSite(key, operation, new ManagedMethodIdentity(target.CanonicalName), target, null));
        }
        var planner = new RecordingDelegateBindingPlanner([]);

        _ = new ReachableProgramBuilder(new EmptyAllocationAnalyzer(), new EmptyByRefEffectAnalyzer(), planner).Build(
            invokeDefinition, [], ReachabilityLedgerSnapshot.From(ledger), new RecognizingDelegateType(delegateType),
            new EmptyTypeTestPlanner());

        Assert.Equal([invoke], planner.Invokes);
    }

    [Fact]
    public void BuilderOrdersBoundaryCollectionsAndPublishesEmptyDispatchTargets()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var owner = new EntityKey(assembly, 1);
        var entry = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            owner,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var invokeDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 3),
            owner,
            "Invoke",
            false,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        var alpha = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "AlphaCallback",
            isValueType: false);
        var zulu = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "ZuluCallback",
            isValueType: false);
        var invoke = new MethodInstanceModel(
            invokeDefinition,
            alpha,
            [],
            invokeDefinition.Signature);
        var firstInitializer = new EntityKey(assembly, 8);
        var secondInitializer = new EntityKey(assembly, 9);
        var firstWitImport = entry with
        {
            Key = new EntityKey(assembly, 10),
            WitImport = new WitImportDeclaration("test:fixture", "alpha"),
        };
        var secondWitImport = entry with
        {
            Key = new EntityKey(assembly, 11),
            WitImport = new WitImportDeclaration("test:fixture", "zulu"),
        };
        var ledger = new ReachabilityLedgerFactory().Create();
        ledger.StaticInitializers.UnionWith([secondInitializer, firstInitializer]);
        ledger.ModuleInitializers.UnionWith([secondInitializer, firstInitializer]);
        ledger.WitImports.Add(secondWitImport.Key, secondWitImport);
        ledger.WitImports.Add(firstWitImport.Key, firstWitImport);
        ledger.AllocatedTypes.UnionWith([zulu, alpha]);
        ledger.HostCallbacks.Add(new HostCallbackDeclaration(
            entry.Key,
            0,
            invoke,
            "zulu"));
        ledger.HostCallbacks.Add(new HostCallbackDeclaration(
            entry.Key,
            1,
            invoke,
            "alpha"));
        ledger.DispatchDeclarations.Add(
            "missing-targets",
            new DispatchDeclaration(
                "Tests.Target::Run",
                4,
                invoke,
                CilOperation.CallVirtual));

        var program = new ReachableProgramBuilder(
            new EmptyAllocationAnalyzer(),
            new EmptyByRefEffectAnalyzer(),
            new EmptyDelegateBindingPlanner()).Build(
                entry,
                [],
                ReachabilityLedgerSnapshot.From(ledger),
                new RecognizingDelegateType(alpha, zulu),
                new EmptyTypeTestPlanner());

        Assert.Equal(2, program.ModuleInitializers.Length);
        Assert.Equal(firstInitializer, program.ModuleInitializers[0]);
        Assert.Equal(secondInitializer, program.ModuleInitializers[1]);
        Assert.Equal(2, program.StaticInitializers.Length);
        Assert.Equal(firstInitializer, program.StaticInitializers[0]);
        Assert.Equal(secondInitializer, program.StaticInitializers[1]);
        Assert.Equal(2, program.WitImportMethods.Length);
        Assert.Equal("alpha", program.WitImportMethods[0].WitImport!.FunctionName);
        Assert.Equal("zulu", program.WitImportMethods[1].WitImport!.FunctionName);
        Assert.Equal(2, program.HostCallbacks.Length);
        Assert.Equal("alpha", program.HostCallbacks[0].ExportName);
        Assert.Equal("zulu", program.HostCallbacks[1].ExportName);
        Assert.Equal(2, program.DelegateTypes.Length);
        Assert.Equal(alpha, program.DelegateTypes[0]);
        Assert.Equal(zulu, program.DelegateTypes[1]);
        Assert.Empty(Assert.Single(program.DispatchCallSites).Value.Targets);
    }

    private sealed class EmptyAllocationAnalyzer : IAllocationCapabilityAnalyzer
    {
        public AllocationCapabilities Analyze(AllocationCapabilityAnalysisRequest request) =>
            new([], []);
    }

    private sealed class EmptyByRefEffectAnalyzer : IManagedByRefEffectAnalyzer
    {
        public ManagedByRefEffects Analyze(ManagedByRefEffectAnalysisRequest request) =>
            ManagedByRefEffects.Empty;
    }

    private sealed class FixedByRefEffectAnalyzer(ManagedByRefEffects result) :
        IManagedByRefEffectAnalyzer
    {
        public int Calls { get; private set; }

        public ManagedByRefEffects Analyze(ManagedByRefEffectAnalysisRequest request)
        {
            Calls++;
            return result;
        }
    }

    private sealed class NoDelegateTypeRecognizer : IDelegateTypeRecognizer
    {
        public bool Recognize(CliTypeIdentity type) => false;
    }

    private sealed class EmptyTypeTestPlanner : ITypeTestPlanner
    {
        public ImmutableDictionary<string, TypeTestSiteModel> Build(
            IEnumerable<ManagedMethodBody> methods,
            IEnumerable<CliTypeIdentity> allocatedTypes) =>
            ImmutableDictionary<string, TypeTestSiteModel>.Empty;
    }

    private sealed class EmptyDelegateBindingPlanner : IDelegateBindingPlanner
    {
        public ImmutableArray<ManagedDelegateBinding> Plan(
            IEnumerable<MethodInstanceModel> invokes,
            IEnumerable<MethodInstanceModel> targets) => [];
    }

    private sealed class RecordingDelegateBindingPlanner(
        ImmutableArray<ManagedDelegateBinding> result) : IDelegateBindingPlanner
    {
        public MethodInstanceModel[] Invokes { get; private set; } = [];
        public MethodInstanceModel[] Targets { get; private set; } = [];

        public ImmutableArray<ManagedDelegateBinding> Plan(
            IEnumerable<MethodInstanceModel> invokes,
            IEnumerable<MethodInstanceModel> targets)
        {
            Invokes = [.. invokes];
            Targets = [.. targets];
            return result;
        }
    }

    private sealed class RecognizingDelegateType(
        params CliTypeIdentity[] expected) : IDelegateTypeRecognizer
    {
        public bool Recognize(CliTypeIdentity type) => expected.Contains(type);
    }
}
