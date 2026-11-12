using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Tests;

public sealed class WholeProgramAnalyzerDirectTests
{
    private sealed class TestNullableTypeResolver(
        IReadOnlyDictionary<CliTypeIdentity, CliTypeIdentity> types) : INullableTypeResolver
    {
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => types.GetValueOrDefault(type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberFieldResultsRetainTheBoxedUnderlyingType(bool nullableResult)
    {
        var fixture = new ClosureFixture { EmptyInstructions = true };
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(fixture.TargetTypeKey.Assembly, "System", "Nullable`1", true), [underlying]);
        var result = nullableResult ? nullable : underlying;
        var field = new FieldInstanceModel(new FieldDefinitionModel(fixture.FieldKey,
            fixture.TargetTypeKey, "Result", result, false), fixture.TargetType, result);
        fixture.MemberExecution = MemberExecutionPlan.Empty with
        {
            Fields = ImmutableDictionary<string, FieldInstanceModel>.Empty.Add(field.CanonicalName, field),
        };
        fixture.NullableTypes.Add(nullable, underlying);

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Contains(underlying, snapshot.AllocatedTypes);
        Assert.DoesNotContain(nullable, snapshot.AllocatedTypes);
        Assert.Contains(fixture.FieldKey, snapshot.Fields);
    }

    [Fact]
    public void EntrylessClosureRootsExportsWithoutInventingAnEntry()
    {
        var fixture = new ClosureFixture { OnlyStaticCall = true };
        var builder = Assert.IsAssignableFrom<IReachabilityClosureBuilder>(fixture.CreateBuilder());
        var export = new ProgramExport("value", fixture.Entry.Key);

        var result = builder.Build(null, [export], new(
            Types: [fixture.TargetTypeKey], Fields: [fixture.FieldKey], Methods: [fixture.Entry.Key]));

        Assert.Null(result.EntryPoint);
        Assert.Equal(export, Assert.Single(result.Exports));
        Assert.Contains(fixture.Entry.Key, fixture.ProgramBuilder.Snapshot!.Methods.Keys);
        Assert.Contains(fixture.TargetTypeKey, fixture.ProgramBuilder.Snapshot.Types);
        Assert.Contains(fixture.FieldKey, fixture.ProgramBuilder.Snapshot.Fields);
        Assert.NotEmpty(fixture.ProgramBuilder.Snapshot.StaticInitializers);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BeforeFieldInitOwnersRetainInitializersOnlyForStaticFieldAccess(
        bool constructed, bool readsField)
    {
        var fixture = new ClosureFixture
        {
            OnlyStaticCall = true,
            ConstructedStaticCall = constructed,
            BeforeFieldInit = true,
            IncludeStaticField = readsField,
        };
        var builder = Assert.IsAssignableFrom<IReachabilityClosureBuilder>(fixture.CreateBuilder());

        _ = builder.Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Equal(readsField, snapshot.MethodInstances.Values.Any(method => method.Definition.Name == ".cctor"));
        Assert.Equal(readsField && !constructed ? 1 : 0, snapshot.StaticInitializers.Count);
        Assert.Equal(readsField && constructed ? 1 : 0, snapshot.ConstructedStaticInitializers.Count);
        Assert.Equal(readsField, snapshot.Fields.Contains(fixture.FieldKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstanceOrInitializerCallsDoNotAddAnImplicitInitializationTrigger(bool initializer)
    {
        var fixture = new ClosureFixture { OnlyStaticCall = true, NonTriggeringCall = initializer ? 2 : 1 };
        var builder = Assert.IsAssignableFrom<IReachabilityClosureBuilder>(fixture.CreateBuilder());

        _ = builder.Build(fixture.Entry, []);

        Assert.Empty(fixture.ProgramBuilder.Snapshot!.StaticInitializers);
        Assert.Empty(fixture.ProgramBuilder.Snapshot.ConstructedStaticInitializers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticCallsRootInitializersWithoutAnyStaticFieldReference(bool constructed)
    {
        var fixture = new ClosureFixture { OnlyStaticCall = true, ConstructedStaticCall = constructed };
        var builder = Assert.IsAssignableFrom<IReachabilityClosureBuilder>(fixture.CreateBuilder());

        _ = builder.Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Empty(snapshot.Fields);
        var initializer = Assert.Single(snapshot.MethodInstances.Values,
            method => method.Definition.Name == ".cctor");
        if (constructed)
        {
            Assert.Contains(initializer.CanonicalName, snapshot.ConstructedStaticInitializers);
            Assert.Equal(CliTypeShape.GenericInstantiation, initializer.DeclaringType.Shape);
            Assert.Equal(CliValueKind.I4, Assert.Single(initializer.DeclaringType.TypeArguments).StackKind);
        }
        else
        {
            Assert.Contains(initializer.Definition.Key, snapshot.StaticInitializers);
        }
    }

    [Fact]
    public void RetainedMethodRootsDoNotImplyStaticInvocation()
    {
        var fixture = new ClosureFixture { EmptyInstructions = true };
        var roots = new ReachabilityRoots([], [], [fixture.Method.Key]);

        _ = fixture.CreateBuilder().Build(fixture.Entry, [], roots);

        Assert.Contains(
            fixture.ProgramBuilder.Snapshot!.MethodInstances.Values,
            method => method.Definition.Key == fixture.Method.Key);
        Assert.DoesNotContain(
            fixture.TargetInitializerKey,
            fixture.ProgramBuilder.Snapshot.StaticInitializers);
    }

    [Fact]
    public void InvokedMethodRootsRetainExplicitStaticInitializers()
    {
        var fixture = new ClosureFixture { EmptyInstructions = true };
        var roots = ReachabilityRoots.Empty with
        {
            InvokedMethods = [fixture.Method.Key],
        };

        _ = fixture.CreateBuilder().Build(fixture.Entry, [], roots);

        Assert.Contains(
            fixture.TargetInitializerKey,
            fixture.ProgramBuilder.Snapshot!.StaticInitializers);
    }

    [Fact]
    public void BeforeFieldInitInvokedRootsDoNotRetainUnusedInitializers()
    {
        var fixture = new ClosureFixture
        {
            EmptyInstructions = true,
            BeforeFieldInit = true,
        };
        var roots = ReachabilityRoots.Empty with
        {
            InvokedMethods = [fixture.Method.Key],
        };

        _ = fixture.CreateBuilder().Build(fixture.Entry, [], roots);

        Assert.DoesNotContain(
            fixture.TargetInitializerKey,
            fixture.ProgramBuilder.Snapshot!.StaticInitializers);
        Assert.DoesNotContain(
            fixture.ProgramBuilder.Snapshot.MethodInstances.Values,
            method => method.Definition.Key == fixture.TargetInitializerKey);
    }

    [Fact]
    public void ClosureBuilderVisitsRuntimeEntitiesFieldsAndDispatchCapabilities()
    {
        var fixture = new ClosureFixture();

        var program = fixture.CreateBuilder().Build(fixture.Entry, []);

        Assert.Contains(fixture.TargetTypeKey, fixture.ProgramBuilder.Snapshot!.Types);
        Assert.Contains(fixture.FieldKey, fixture.ProgramBuilder.Snapshot.Fields);
        Assert.Contains(fixture.Method.Key, fixture.ProgramBuilder.Snapshot.MethodInstances.Values
            .Select(instance => instance.Definition.Key));
        Assert.NotEmpty(fixture.ProgramBuilder.Snapshot.Finalizers);
        Assert.NotEmpty(fixture.ProgramBuilder.Snapshot.StaticInitializers);
        Assert.NotEmpty(fixture.ProgramBuilder.Snapshot.DispatchDeclarations);
        Assert.Contains(ManagedExceptionKind.Argument,
            fixture.ProgramBuilder.Snapshot.ImplicitExceptions);
        // managed.throw calls the kind's CoreLib getter, so it is reached.
        Assert.Contains(
            ExceptionGetters.Methods.Single(method =>
                method.Name == RuntimeExceptionGetters.MethodName(ManagedExceptionKind.Argument)).Key,
            fixture.ProgramBuilder.Snapshot.MethodInstances.Values.Select(instance => instance.Definition.Key));
        Assert.Contains(fixture.DelegateType,
            fixture.ProgramBuilder.Snapshot.AllocatedTypes);
        Assert.Equal(
            fixture.ProgramBuilder.Snapshot.DispatchDeclarations.Count *
                fixture.ProgramBuilder.Snapshot.AllocatedTypes.Count,
            fixture.DispatchRequests.Count);
        Assert.Equal(
            fixture.DispatchRequests.Count,
            fixture.DispatchRequests.Distinct().Count());
        Assert.Equal(
            fixture.DispatchRequests.Count,
            fixture.ProgramBuilder.Snapshot.DispatchTargets.Single().Value.Length);
    }

    [Fact]
    public void MemberDescriptorsRetainMetadataWithoutMethodBodiesOrFieldStorage()
    {
        var fixture = new ClosureFixture { DescriptorOnly = true };

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Equal(4, snapshot.MethodDescriptors.Count);
        var method = snapshot.MethodDescriptors[fixture.MethodInstance.CanonicalName];
        var field = Assert.Single(snapshot.FieldDescriptors).Value;
        Assert.Equal(fixture.Method.Key, method.Definition.Key);
        Assert.Equal(fixture.FieldKey, field.Definition.Key);
        Assert.DoesNotContain(
            snapshot.MethodInstances.Values,
            candidate => candidate.Definition.Key == fixture.Method.Key);
        Assert.DoesNotContain(fixture.FieldKey, snapshot.Fields);
        Assert.Empty(snapshot.StaticInitializers);
        Assert.Empty(snapshot.ConstructedStaticInitializers);
        Assert.Empty(snapshot.NamedMemberDescriptors);
        Assert.Empty(snapshot.Strings);
        Assert.True(snapshot.Types.Count > 1);
    }

    [Fact]
    public void MemberNameDemandRootsOnlyNamesForReachableDescriptors()
    {
        var fixture = new ClosureFixture
        {
            DescriptorOnly = true,
            DemandMemberNames = true,
        };

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Equal(
            snapshot.MethodDescriptors.Count + snapshot.FieldDescriptors.Count,
            snapshot.NamedMemberDescriptors.Count);
        Assert.All(
            snapshot.MethodDescriptors.Keys,
            identity => Assert.Contains(identity, snapshot.NamedMemberDescriptors));
        Assert.All(
            snapshot.FieldDescriptors.Keys,
            identity => Assert.Contains(identity, snapshot.NamedMemberDescriptors));
        Assert.Equal(
            snapshot.MethodDescriptors.Values
                .Select(method => method.Definition.Name)
                .Concat(snapshot.FieldDescriptors.Values.Select(field =>
                    field.Definition.Name))
                .ToHashSet(StringComparer.Ordinal),
            snapshot.Strings);
    }

    [Fact]
    public void PropertyAccessorDescriptorRetainsSiblingDescriptorWithoutEitherBody()
    {
        var fixture = new ClosureFixture
        {
            DescriptorOnly = true,
            IncludePropertyDescriptor = true,
        };

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        var property = Assert.Single(snapshot.PropertyDescriptors).Value;
        Assert.NotNull(property.Getter);
        Assert.NotNull(property.Setter);
        Assert.Contains(property.Getter.CanonicalName, snapshot.MethodDescriptors);
        Assert.Contains(property.Setter.CanonicalName, snapshot.MethodDescriptors);
        Assert.DoesNotContain(
            snapshot.MethodInstances.Values,
            candidate => candidate.Definition.Key == property.Getter.Definition.Key);
        Assert.DoesNotContain(
            snapshot.MethodInstances.Values,
            candidate => candidate.Definition.Key == property.Setter.Definition.Key);
        Assert.Empty(snapshot.Fields);
        Assert.Empty(snapshot.StaticInitializers);
        Assert.Empty(snapshot.ConstructedStaticInitializers);
    }

    [Fact]
    public void LatePropertyDescriptorAllocationCompletesRecordedDispatchClosure()
    {
        var fixture = new ClosureFixture
        {
            DescriptorOnly = true,
            IncludePropertyDescriptor = true,
            LatePropertyDispatch = true,
        };

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Contains(fixture.RuntimePropertyInfoType, snapshot.AllocatedTypes);
        Assert.Contains(
            snapshot.MethodInstances.Values,
            candidate => candidate.Definition.Key == fixture.Method.Key);
        var target = Assert.Single(snapshot.DispatchTargets).Value;
        Assert.Contains(
            target,
            candidate => candidate.ReceiverType == fixture.RuntimePropertyInfoType);
    }

    [Fact]
    public void IntrinsicArrayRootsJoinRuntimeConstructedAndAllocationClosure()
    {
        var fixture = new ClosureFixture { EmptyInstructions = true };
        var array = CliTypeIdentity.SzArray(fixture.LateIntrinsicRoot);
        fixture.IntrinsicRootPlan = new([fixture.LateIntrinsicRoot, array], [fixture.LateIntrinsicRoot, array]);

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Contains(fixture.LateIntrinsicRootKey, snapshot.Types);
        Assert.Contains(array, snapshot.ConstructedTypes);
        Assert.Contains(fixture.LateIntrinsicRoot, snapshot.AllocatedTypes);
        Assert.Contains(array, snapshot.AllocatedTypes);
        Assert.Contains(fixture.TargetType, fixture.FixedIntrinsicRoots!.ObservedTypes);
        Assert.Contains(array, fixture.FixedIntrinsicRoots.ObservedTypes);
    }

    [Fact]
    public void LateDescriptorTypeFeedsEarlierDerivedRootPhaseAtFixedPoint()
    {
        var fixture = new ClosureFixture
        {
            DescriptorOnly = true,
            IncludePropertyDescriptor = true,
            LateIntrinsicDescriptorType = true,
        };

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        Assert.True(fixture.IntrinsicRoots!.Calls >= 2);
        Assert.True(fixture.IntrinsicRoots.SawTrigger);
        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Contains(ClosureFixture.LateDescriptorModifier, snapshot.ConstructedTypes);
        Assert.Contains(ClosureFixture.Int32TypeKey, snapshot.Types);
        Assert.Contains(
            fixture.LateIntrinsicRootKey,
            snapshot.Types);
    }

    [Fact]
    public void NativeCallbackAndOpenGenericFactsReachTheirDedicatedLedgers()
    {
        var fixture = new ClosureFixture
        {
            IncludeNativeCallback = true,
            IncludeOpenGenericRuntimeType = true,
        };

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        Assert.Single(fixture.ProgramBuilder.Snapshot!.NativeCallbacks);
    }

    [Fact]
    public void RepeatedPropertyPublicationRejectsContradictoryFacts()
    {
        var fixture = new ClosureFixture
        {
            DescriptorOnly = true,
            IncludePropertyDescriptor = true,
            ContradictoryRepeatedProperty = true,
        };

        var exception = Assert.Throws<CompilerException>(() =>
            fixture.CreateBuilder().Build(fixture.Entry, []));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void ClosureBuilderClassifiesEntryAndRequestedExportsAsOutwardBoundaries()
    {
        var fixture = new ClosureFixture();
        var builder = Assert.IsAssignableFrom<IReachabilityClosureBuilder>(
            fixture.CreateBuilder());

        _ = builder.Build(
            fixture.Entry,
            [new ProgramExport("call", fixture.Method.Key)]);

        Assert.Contains(
            fixture.Imports.Requests,
            request => request.Method.Definition.Key == fixture.Entry.Key &&
                       request.IsOutwardBoundary);
        Assert.Contains(
            fixture.Imports.Requests,
            request => request.Method.Definition.Key == fixture.Method.Key &&
                       request.IsOutwardBoundary);
    }

    [Fact]
    public void ClosureBuilderWrapsRuntimeTypeResolutionFailures()
    {
        var fixture = new ClosureFixture
        {
            ThrowRuntimeTypeResolution = true,
        };

        var error = Assert.Throws<CompilerException>(() =>
            fixture.CreateBuilder().Build(fixture.Entry, []));

        Assert.Contains("could not be resolved", error.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedConstructedCatchIdentityIsRetainedWithoutAllocation()
    {
        var definition = CliTypeIdentity.Named(
            new AssemblyIdentity("in-memory"),
            "Fixtures",
            "Failure`1",
            false);
        var identity = CliTypeIdentity.GenericInstantiation(
            definition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var fixture = new ClosureFixture { CatchTypeIdentity = identity };

        _ = fixture.CreateBuilder().Build(fixture.Entry, []);

        var snapshot = fixture.ProgramBuilder.Snapshot!;
        Assert.Contains(identity, snapshot.ConstructedTypes);
        Assert.DoesNotContain(identity, snapshot.AllocatedTypes);
    }

    [Fact]
    public void OpenCatchIdentityIsRejectedBeforeExecutableReachability()
    {
        var identity = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                new AssemblyIdentity("in-memory"),
                "Fixtures",
                "Failure`1",
                false),
            [CliTypeIdentity.GenericParameter(method: false, 0)]);
        var fixture = new ClosureFixture { CatchTypeIdentity = identity };

        var error = Assert.Throws<CompilerException>(() =>
            fixture.CreateBuilder().Build(fixture.Entry, []));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
        Assert.Contains("catch type must be closed", error.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ClosureBuilderRejectsEveryUnresolvedGenericMethodShape(int kind)
    {
        var fixture = new ClosureFixture
        {
            InvalidGenericKind = kind,
        };

        var error = Assert.Throws<CompilerException>(() =>
            fixture.CreateBuilder().Build(fixture.Entry, []));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    public void ClosureBuilderValidatesEveryInjectedCapability(int missing)
    {
        var fixture = new ClosureFixture();

        var exception = Assert.Throws<ArgumentNullException>(() => fixture.CreateBuilder(missing));
        if (missing == 28)
            Assert.Equal("initializationTriggers", exception.ParamName);
    }

    private sealed class ClosureFixture
    {
        private static readonly AssemblyIdentity Assembly = new("in-memory");

        internal readonly EntityKey TargetTypeKey = new(Assembly, 100);
        internal readonly EntityKey MethodKey = new(Assembly, 1);
        internal readonly EntityKey FieldKey = new(Assembly, 200);
        internal readonly EntityKey DelegateTypeKey = new(Assembly, 101);
        internal readonly CliTypeIdentity TargetType = CliTypeIdentity.Named(
            Assembly, "Fixtures", "Target", false);
        internal readonly CliTypeIdentity DelegateType;
        internal readonly CliTypeIdentity RuntimeType = CliTypeIdentity.Named(
            Assembly, "Fixtures", "Runtime", false);
        internal readonly CliTypeIdentity RuntimePropertyInfoType = CliTypeIdentity.Named(
            new AssemblyIdentity("runtime"),
            "System.Reflection",
            "RuntimePropertyInfo",
            false);
        internal readonly CliTypeIdentity LateIntrinsicRoot = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "LateIntrinsicRoot",
            false);
        internal readonly MethodDefinitionModel Entry;
        internal readonly MethodDefinitionModel Method;
        internal readonly MethodInstanceModel MethodInstance;
        internal readonly ReachabilityProgramRecorder ProgramBuilder = new();
        internal readonly RecordingImportClassifier Imports = new();
        internal MemberExecutionPlan MemberExecution { get; set; } = MemberExecutionPlan.Empty;
        internal Dictionary<CliTypeIdentity, CliTypeIdentity> NullableTypes { get; } = [];
        private FixedDispatchResolver? _dispatchTargets;

        internal List<(MethodInstanceModel Declaration, CliTypeIdentity Receiver)>
            DispatchRequests => _dispatchTargets!.Requests;
        internal bool ThrowRuntimeTypeResolution { get; init; }
        internal int InvalidGenericKind { get; init; } = -1;
        internal bool OnlyStaticCall { get; init; }
        internal bool ConstructedStaticCall { get; init; }
        internal bool BeforeFieldInit { get; init; }
        internal bool IncludeStaticField { get; init; }
        internal bool EmptyInstructions { get; init; }
        internal bool DescriptorOnly { get; init; }
        internal bool IncludePropertyDescriptor { get; init; }
        internal bool DemandMemberNames { get; init; }
        internal bool IncludeNativeCallback { get; init; }
        internal bool IncludeOpenGenericRuntimeType { get; init; }
        internal int NonTriggeringCall { get; init; }
        internal bool LatePropertyDispatch { get; init; }
        internal bool LateIntrinsicDescriptorType { get; init; }
        internal bool ContradictoryRepeatedProperty { get; init; }
        internal CliTypeIdentity? CatchTypeIdentity { get; init; }
        internal RuntimeIntrinsicTypeRootPlan IntrinsicRootPlan { get; set; } = RuntimeIntrinsicTypeRootPlan.Empty;
        internal FixedRuntimeIntrinsicTypeRootPlanner? FixedIntrinsicRoots { get; private set; }
        internal ConditionalRuntimeIntrinsicTypeRootPlanner? IntrinsicRoots { get; private set; }
        internal static CliTypeIdentity LateDescriptorModifier =>
            CliTypeIdentity.UnmanagedPointer(
                CliTypeIdentity.FromStackKind(CliValueKind.I4));
        internal static EntityKey Int32TypeKey => new(
            new AssemblyIdentity("runtime"),
            "System.Int32".GetHashCode());
        internal EntityKey LateIntrinsicRootKey => new(
            new AssemblyIdentity("runtime"),
            LateIntrinsicRoot.CanonicalName.GetHashCode());
        internal EntityKey TargetInitializerKey => _initializer.Key;

        private readonly MethodDefinitionModel _finalizer;
        private readonly MethodDefinitionModel _initializer;
        private readonly MethodDefinitionModel _propertyGetter;
        private readonly MethodDefinitionModel _propertySetter;
        private readonly MethodDefinitionModel _delegateInvoke;
        private readonly CliTypeIdentity _delegateDefinitionType;
        private readonly PropertyDefinitionModel _property;
        private readonly FieldDefinitionModel _field;
        private readonly TypeDefinitionModel _targetDefinition;

        internal ClosureFixture()
        {
            _delegateDefinitionType = CliTypeIdentity.Named(Assembly, "Fixtures", "Callback", false);
            DelegateType = _delegateDefinitionType;
            Entry = new(
                MethodKey,
                TargetTypeKey,
                "Run",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            Method = new(
                new EntityKey(Assembly, 2),
                TargetTypeKey,
                "Call",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1)
            {
                IsVirtual = true,
            };
            MethodInstance = new(Method, TargetType, [], Method.Signature);
            _finalizer = new(
                new EntityKey(Assembly, 3),
                TargetTypeKey,
                "Finalize",
                false,
                MethodSignatureModel.Create(CliValueKind.Void),
                1)
            {
                IsVirtual = true,
            };
            _initializer = new(
                new EntityKey(Assembly, 4),
                TargetTypeKey,
                ".cctor",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            _propertyGetter = new(
                new EntityKey(Assembly, 8),
                TargetTypeKey,
                "get_Value",
                false,
                MethodSignatureModel.Create(CliValueKind.I4),
                1);
            _propertySetter = new(
                new EntityKey(Assembly, 9),
                TargetTypeKey,
                "set_Value",
                false,
                MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
                1);
            var delegateParameter = CliTypeIdentity.FromStackKind(CliValueKind.I4);
            _delegateInvoke = new(
                new EntityKey(Assembly, 10),
                DelegateTypeKey,
                "Invoke",
                false,
                MethodSignatureModel.Create(delegateParameter, delegateParameter),
                0)
            {
                IsPublic = true,
                IsVirtual = true,
                IsAbstract = true,
            };
            _property = new(
                new EntityKey(Assembly, 300),
                TargetTypeKey,
                "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [CliTypeIdentity.FromStackKind(CliValueKind.I8)],
                _propertyGetter.Key,
                _propertySetter.Key);
            _field = new(
                FieldKey,
                TargetTypeKey,
                "Value",
                CliValueKind.I4,
                true)
            {
                SignatureType = CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            };
            _targetDefinition = new(
                TargetTypeKey,
                "Fixtures",
                "Target",
                false,
                [FieldKey],
                [_finalizer.Key, _initializer.Key]);
        }

        internal ReachabilityClosureBuilder CreateBuilder(int missing = -1)
        {
            var modifier = LateDescriptorModifier;
            var propertySetter = LateIntrinsicDescriptorType
                ? _propertySetter with
                {
                    Signature = MethodSignatureModel.Create(
                        CliTypeIdentity.FromStackKind(CliValueKind.Void),
                        modifier),
                }
                : _propertySetter;
            IMethodRepository methods = new FixedMethodRepository(
                Entry,
                Method,
                _finalizer,
                _initializer,
                _propertyGetter,
                propertySetter,
                _delegateInvoke);
            var field = new FieldInstanceModel(_field, TargetType, _field.SignatureType);
            var target = MethodInstance;
            if (NonTriggeringCall != 0)
            {
                var called = NonTriggeringCall == 1 ? Method with { IsStatic = false } : _initializer;
                target = new(called, TargetType, [], called.Signature);
                methods = new FixedMethodRepository(Entry,
                    NonTriggeringCall == 1 ? called : Method, _finalizer, _initializer);
            }
            if (ConstructedStaticCall)
            {
                target = target with
                {
                    DeclaringType = CliTypeIdentity.GenericInstantiation(TargetType,
                    [CliTypeIdentity.FromStackKind(CliValueKind.I4)])
                };
            }
            var instructions = CreateInstructions(target, field);
            if (EmptyInstructions)
            {
                instructions = new([], [], [], [], [], [], [], [], [], [], []);
            }
            if (DescriptorOnly)
            {
                var parameter = CliTypeIdentity.FromStackKind(CliValueKind.I4);
                var methodArgument = CliTypeIdentity.FromStackKind(CliValueKind.I8);
                var constructor = Descriptor(
                    5,
                    ".ctor",
                    MethodSignatureModel.Create(
                        CliTypeIdentity.FromStackKind(CliValueKind.Void),
                        parameter));
                var staticConstructor = Descriptor(
                    6,
                    ".cctor",
                    MethodSignatureModel.Create(CliValueKind.Void));
                var genericMethod = Descriptor(
                    7,
                    "Generic",
                    MethodSignatureModel.Create(parameter, methodArgument),
                    [methodArgument]);
                instructions = new([], [], [], [], [], [], [], [], [], [], [])
                {
                    MethodDescriptors =
                    [
                        target, target,
                        constructor, constructor,
                        staticConstructor, staticConstructor,
                        genericMethod, genericMethod,
                    ],
                    FieldDescriptors = [field, field],
                };
                if (IncludePropertyDescriptor)
                {
                    instructions = instructions with
                    {
                        MethodDescriptors = instructions.MethodDescriptors.Add(
                            new MethodInstanceModel(
                                _propertyGetter,
                                TargetType,
                                [],
                                _propertyGetter.Signature)),
                    };
                    if (LatePropertyDispatch)
                    {
                        instructions = instructions with
                        {
                            Dispatches =
                            [
                                new(
                                    "late-property-dispatch",
                                    new DispatchDeclaration(
                                        "System.Reflection.PropertyInfo::get_Name",
                                        4,
                                        target,
                                        CilOperation.CallVirtual)),
                            ],
                        };
                    }
                }
            }
            if (OnlyStaticCall)
            {
                instructions = new([], [], [], [], [], [new(CilOperation.Call, target)], [], [], [], [], []);
                if (IncludeStaticField)
                {
                    instructions = instructions with { Fields = [field with { DeclaringType = target.DeclaringType }] };
                }
            }
            if (DemandMemberNames)
            {
                instructions = instructions with { RequiresMemberNames = true };
            }
            if (IncludeNativeCallback)
            {
                var callback = target with
                {
                    Definition = target.Definition with
                    {
                        NativeCallback = new([], null, false, false),
                    },
                };
                instructions = instructions with
                {
                    NativeCallbacks = [callback],
                };
            }
            if (IncludeOpenGenericRuntimeType)
            {
                var openGeneric = CliTypeIdentity.GenericInstantiation(
                    CliTypeIdentity.Named(
                        Assembly,
                        "Fixtures",
                        "Open`1",
                        isValueType: false),
                    [CliTypeIdentity.GenericParameter(method: false, 0)]);
                instructions = instructions with
                {
                    ConstructedTypes = instructions.ConstructedTypes.Add(openGeneric),
                    RequiresTypeFacts = true,
                };
            }
            if (InvalidGenericKind >= 0)
            {
                var invalid = CreateInvalidGenericMethod(InvalidGenericKind);
                instructions = instructions with
                {
                    Methods = instructions.Methods.Add(
                        new ReachabilityMethodReference(CilOperation.Call, invalid)),
                };
                methods = new FixedMethodRepository(
                    Entry,
                    Method,
                    _finalizer,
                    _initializer,
                    invalid.Definition);
            }
            var methodAnalyzer = new FixedMethodAnalyzer(instructions, CatchTypeIdentity);
            var definition = _targetDefinition with { IsBeforeFieldInit = BeforeFieldInit };
            var delegateDefinition = new TypeDefinitionModel(
                DelegateTypeKey,
                "Fixtures",
                "Callback",
                false,
                [],
                [_delegateInvoke.Key]);
            ITypeDefinitionResolver typeDefinitions = new FixedTypeDefinitionResolver(
                definition,
                TargetType,
                delegateDefinition,
                DelegateType,
                RuntimeType,
                ThrowRuntimeTypeResolution);
            IBaseTypeResolver baseTypes = new EmptyBaseTypeResolver();
            ITypeIdentityResolver typeIdentities = new FixedTypeIdentityResolver(
                TargetType,
                RuntimePropertyInfoType);
            IDelegateTypeRecognizer delegateTypes = new DelegateRecognizer(DelegateType);
            var typeRepository = new FixedTypeRepository(definition);
            var fieldRepository = new FixedFieldRepository(_field);
            var typeFinder = new FixedTypeFinder();
            var methodInstances = new FixedMethodInstanceResolver(
                TargetType,
                TargetTypeKey,
                DelegateType,
                _delegateDefinitionType,
                DelegateTypeKey,
                methods);
            var symbols = new FixedSymbolFormatter(TargetType);
            _dispatchTargets = new FixedDispatchResolver(
                target,
                LatePropertyDispatch ? RuntimePropertyInfoType : null);
            var typeTests = new EmptyTypeTestPlanner();
            var ledgers = new ReachabilityLedgerFactory();
            IntrinsicRoots = LateIntrinsicDescriptorType
                ? new ConditionalRuntimeIntrinsicTypeRootPlanner(
                    modifier,
                    LateIntrinsicRoot)
                : null;
            FixedIntrinsicRoots = new(IntrinsicRootPlan);
            IRuntimeIntrinsicTypeRootPlanner intrinsicRoots = IntrinsicRoots is null
                ? FixedIntrinsicRoots
                : IntrinsicRoots;
            IMetadataPropertyAccessorResolver propertyAccessors =
                IncludePropertyDescriptor
                    ? ContradictoryRepeatedProperty
                        ? new ChangingPropertyAccessorResolver(
                            new MetadataPropertyAccessorResolver(
                                [_property],
                                methodInstances),
                            _property,
                            TargetType,
                            _propertyGetter,
                            propertySetter)
                        : new MetadataPropertyAccessorResolver(
                            [_property],
                            methodInstances)
                    : new EmptyPropertyAccessorResolver();
            return new ReachabilityClosureBuilder(
                missing == 0 ? null! : typeRepository,
                missing == 1 ? null! : fieldRepository,
                missing == 2 ? null! : methods,
                missing == 3 ? null! : typeFinder,
                missing == 4 ? null! : typeDefinitions,
                missing == 5 ? null! : typeIdentities,
                missing == 6 ? null! : methodInstances,
                missing == 7 ? null! : symbols,
                missing == 8 ? null! : _dispatchTargets,
                missing == 9 ? null! : delegateTypes,
                missing == 10 ? null! : Imports,
                missing == 17
                    ? null!
                    : new SynchronousReachableMethodAnalysisSessionFactory(
                        missing == 11 ? null! : methodAnalyzer,
                        new RecordingReachableMethodBatchObserver()),
                missing == 12 ? null! : typeTests,
                missing == 13 ? null! : ProgramBuilder,
                missing == 14 ? null! : ledgers,
                missing == 18 ? null! : new ManagedCallSiteLedgerWriter(),
                missing == 21 ? null! : intrinsicRoots,
                missing == 16 ? null! : new RecordingReachabilityClosureObserver(),
                missing == 15
                    ? null!
                    : new DispatchCandidateIndexFactory(
                        new TestTypeRelationshipClassifier(), UnboundedTypeRelationshipKeys.Instance),
                missing == 19 ? null! : new ModuleInitializerResolver([], []),
                missing == 20
                    ? null!
                    : new ModuleInitializerOrderer(
                        new Dictionary<AssemblyIdentity, IReadOnlyList<AssemblyIdentity>>()),
                missing == 22
                    ? null!
                    : new MemberDescriptorPlanner(propertyAccessors),
                missing == 24
                    ? null!
                    : new EmptyObjectArrayDelegateAdapterPlanner(),
                missing == 25 ? null! : new FixedMemberExecutionPlanner(MemberExecution),
                missing == 26
                    ? null!
                    : new FinalizerResolver(typeDefinitions, methods, baseTypes),
                missing == 27 ? null! : new TestNullableTypeResolver(NullableTypes),
                missing == 28 ? null! : new MethodInitializationTriggerClassifier(typeRepository),
                missing == 29 ? null! : new NoIntrinsicAddressBodyPlanner(),
                missing == 30 ? null! : new RuntimeTypeFactsClosurePlanner(baseTypes, typeDefinitions, typeIdentities));

            MethodInstanceModel Descriptor(
                int token,
                string name,
                MethodSignatureModel signature,
                ImmutableArray<CliTypeIdentity> methodArguments = default)
            {
                var definition = new MethodDefinitionModel(
                    new EntityKey(Assembly, token),
                    TargetTypeKey,
                    name,
                    true,
                    signature,
                    0)
                {
                    GenericArity = methodArguments.IsDefaultOrEmpty ? 0 : methodArguments.Length,
                };
                return new(
                    definition,
                    TargetType,
                    methodArguments.IsDefault ? [] : methodArguments,
                    signature);
            }
        }

        private MethodInstanceModel CreateInvalidGenericMethod(int kind)
        {
            var genericParameter = CliTypeIdentity.GenericParameter(true, 0);
            var signature = new MethodSignatureModel(
                kind == 3 ? genericParameter : CliTypeIdentity.FromStackKind(CliValueKind.Void),
                kind == 4
                    ? [genericParameter]
                    : ImmutableArray<CliTypeIdentity>.Empty);
            var definition = new MethodDefinitionModel(
                new EntityKey(Assembly, 500 + kind),
                kind == 1 ? new EntityKey(Assembly, 501) : TargetTypeKey,
                "Open",
                true,
                signature,
                1)
            {
                GenericArity = kind is 0 or 2 ? 1 : 0,
            };
            var declaringType = kind == 1
                ? genericParameter
                : TargetType;
            var arguments = kind == 2
                ? [genericParameter]
                : ImmutableArray<CliTypeIdentity>.Empty;
            return new MethodInstanceModel(definition, declaringType, arguments, signature);
        }

        private ReachabilityInstructionAnalysis CreateInstructions(
            MethodInstanceModel target,
            FieldInstanceModel field)
        {
            var primitives = new[]
            {
                CliTypeIdentity.Primitive("void", CliValueKind.Void),
                CliTypeIdentity.Primitive("bool", CliValueKind.I4),
                CliTypeIdentity.Primitive("char", CliValueKind.I4),
                CliTypeIdentity.Primitive("i1", CliValueKind.I4),
                CliTypeIdentity.Primitive("u1", CliValueKind.I4),
                CliTypeIdentity.Primitive("i2", CliValueKind.I4),
                CliTypeIdentity.Primitive("u2", CliValueKind.I4),
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                CliTypeIdentity.Primitive("u4", CliValueKind.I4),
                CliTypeIdentity.Primitive("i8", CliValueKind.I8),
                CliTypeIdentity.Primitive("u8", CliValueKind.I8),
                CliTypeIdentity.Primitive("f4", CliValueKind.F4),
                CliTypeIdentity.Primitive("f8", CliValueKind.F8),
                CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
                CliTypeIdentity.Primitive("nativeuint", CliValueKind.NativeInt),
                CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false),
                CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, false),
            };
            var constructed = CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false),
                [primitives[7]]);
            var array = CliTypeIdentity.SzArray(primitives[7]);
            return new(
                [.. primitives, RuntimeType],
                [constructed, array],
                [DelegateType],
                [TargetTypeKey],
                ["literal"],
                [
                    new(CilOperation.Call, target),
                ],
                [
                    new(CilOperation.NewArray, TargetTypeKey),
                    new(CilOperation.CastClass, TargetTypeKey),
                    new(CilOperation.IsInstance, TargetTypeKey),
                    new(CilOperation.Call, target.Definition.Key),
                    new(CilOperation.CallVirtual, target.Definition.Key),
                    new(CilOperation.NewObject, target.Definition.Key),
                    new(CilOperation.LoadStaticField, FieldKey),
                    new(CilOperation.Box, TargetTypeKey),
                ],
                [field],
                [
                    new(
                        "dispatch",
                        new DispatchDeclaration("Fixtures.Target::Run", 4, target,
                            CilOperation.LoadVirtualFunction)),
                ],
                [target], []);
        }
    }

    private sealed class FixedTypeRepository(TypeDefinitionModel definition) : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => definition;
    }

    private sealed class FixedFieldRepository(FieldDefinitionModel field) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => field;
    }

    private sealed class FixedMethodRepository(params MethodDefinitionModel[] definitions) :
        IMethodRepository
    {
        private readonly ImmutableDictionary<EntityKey, MethodDefinitionModel> _methods =
            definitions.Concat(ExceptionGetters.Methods).ToImmutableDictionary(method => method.Key);

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
    }

    private sealed class FixedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => new(
            Key(fullName),
            fullName.Contains('.') ? fullName[..fullName.LastIndexOf('.')] : "",
            fullName.Contains('.') ? fullName[(fullName.LastIndexOf('.') + 1)..] : fullName,
            false,
            [],
            fullName == RuntimeExceptionGetters.TypeName
                ? [.. ExceptionGetters.Methods.Select(static method => method.Key)]
                : []);

        public static EntityKey Key(string fullName) =>
            new(new AssemblyIdentity("runtime"), fullName.GetHashCode());
    }

    // CoreLib's getter of each failed check's exception, which managed.throw
    // calls, so a closure with an implicit exception reaches it.
    private static class ExceptionGetters
    {
        public static readonly EntityKey TypeKey = FixedTypeFinder.Key(RuntimeExceptionGetters.TypeName);

        public static readonly CliTypeIdentity Type = CliTypeIdentity.Named(
            new AssemblyIdentity("runtime"), "System", "RuntimeExceptionHelpers", isValueType: false);

        public static readonly ImmutableArray<MethodDefinitionModel> Methods =
        [
            .. Enum.GetValues<ManagedExceptionKind>().Select(static kind => new MethodDefinitionModel(
                FixedTypeFinder.Key($"{RuntimeExceptionGetters.TypeName}::{RuntimeExceptionGetters.MethodName(kind)}"),
                TypeKey,
                RuntimeExceptionGetters.MethodName(kind),
                true,
                MethodSignatureModel.Create(CliValueKind.ManagedReference),
                1)),
        ];
    }

    private sealed class FixedTypeDefinitionResolver(
        TypeDefinitionModel target,
        CliTypeIdentity targetType,
        TypeDefinitionModel delegateDefinition,
        CliTypeIdentity delegateType,
        CliTypeIdentity runtimeType,
        bool throwRuntimeType) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            if (throwRuntimeType && identity.Equals(runtimeType))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    "missing runtime type"));
            }
            return identity.Equals(runtimeType) || identity.Equals(targetType)
                ? target
                : identity.Equals(delegateType)
                    ? delegateDefinition
                    : new(
                new EntityKey(new AssemblyIdentity("runtime"), identity.CanonicalName.GetHashCode()),
                "Fixtures",
                "Type",
                identity.IsValueType,
                [],
                []);
        }
    }

    private sealed class FixedTypeIdentityResolver(
        CliTypeIdentity type,
        CliTypeIdentity runtimePropertyInfoType) : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey key) =>
            key == new EntityKey(
                new AssemblyIdentity("runtime"),
                "System.Reflection.RuntimePropertyInfo".GetHashCode())
                ? runtimePropertyInfoType
                : type;
    }

    private sealed class FixedMethodInstanceResolver(
        CliTypeIdentity declaringType,
        EntityKey declaringTypeKey,
        CliTypeIdentity delegateType,
        CliTypeIdentity delegateDefinitionType,
        EntityKey delegateTypeKey,
        IMethodRepository methods) : IMethodInstanceResolver
    {
        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null)
        {
            var definition = methods.GetMethod(new EntityKey(source, metadataToken));
            var definitionDeclaringType = definition.DeclaringType == delegateTypeKey
                ? delegateDefinitionType
                : definition.DeclaringType == declaringTypeKey
                    ? declaringType
                    : definition.DeclaringType == ExceptionGetters.TypeKey
                        ? ExceptionGetters.Type
                        : throw new InvalidOperationException(
                            "The in-memory method declaring type is unknown.");
            var context = genericContext?.Normalize() ?? CliGenericContext.Empty;
            return new(
                definition,
                context.TypeArguments.IsEmpty
                    ? definition.DeclaringType == delegateTypeKey
                        ? delegateType
                        : definitionDeclaringType
                    : CliTypeIdentity.GenericInstantiation(
                        definitionDeclaringType,
                        context.TypeArguments),
                context.MethodArguments,
                definition.Signature.Substitute(
                    context.TypeArguments,
                    context.MethodArguments));
        }
    }

    private sealed class FixedSymbolFormatter(CliTypeIdentity target) : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == new EntityKey(new AssemblyIdentity("in-memory"), 100)
            ? target.FullName!
            : key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class FixedDispatchResolver(
        MethodInstanceModel target,
        CliTypeIdentity? receiverFilter) : IDispatchTargetResolver
    {
        public List<(MethodInstanceModel Declaration, CliTypeIdentity Receiver)> Requests
        { get; } = [];

        public DispatchTargetModel? Resolve(
            MethodInstanceModel declaration,
            CliTypeIdentity receiver)
        {
            Requests.Add((declaration, receiver));
            return receiverFilter is null || receiver == receiverFilter
                ? new(receiver, target)
                : null;
        }
    }

    private sealed class DelegateRecognizer(CliTypeIdentity delegateType) : IDelegateTypeRecognizer
    {
        public bool Recognize(CliTypeIdentity type) => type.Equals(delegateType);
    }

    private sealed class RecordingImportClassifier : IReachabilityImportClassifier
    {
        public List<ReachabilityImportRequest> Requests { get; } = [];

        public ReachabilityImportAnalysis Classify(ReachabilityImportRequest request)
        {
            Requests.Add(request);
            return new(
                false,
                [],
                [],
                [],
                [],
                [],
                [],
                null,
                null,
                [],
                null);
        }
    }

    private sealed class FixedMethodAnalyzer(
        ReachabilityInstructionAnalysis instructions,
        CliTypeIdentity? catchTypeIdentity) :
        IReachableMethodAnalyzer
    {
        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
        {
            var body = catchTypeIdentity is null
                ? ValidationTestData.ManagedMethodBody(
                    [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())],
                    request.Method.Definition,
                    request.Method)
                : ValidationTestData.ManagedMethodBody(
                    [
                        new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                        new CilInstruction(1, 2, CilOperation.Leave, new CilOperand.BranchTarget(4)),
                        new CilInstruction(2, 3, CilOperation.Pop, new CilOperand.None()),
                        new CilInstruction(3, 4, CilOperation.Leave, new CilOperand.BranchTarget(4)),
                        new CilInstruction(4, 5, CilOperation.Return, new CilOperand.None()),
                    ],
                    request.Method.Definition,
                    request.Method,
                    [
                        new CilExceptionRegion(
                            CilExceptionRegionKind.Catch,
                            0,
                            2,
                            2,
                            2,
                            null,
                            null)
                        {
                            CatchTypeIdentity = catchTypeIdentity,
                        },
                    ]);
            return new(
                request.Method,
                body,
                [request.Method.Definition.DeclaringType],
                [new(ManagedExceptionKind.Argument, "System.ArgumentException")],
                instructions);
        }
    }

    private sealed class EmptyTypeTestPlanner : ITypeTestPlanner
    {
        public ImmutableDictionary<string, TypeTestSiteModel> Build(
            IEnumerable<ManagedMethodBody> methods,
            IEnumerable<CliTypeIdentity> allocatedTypes) => [];
    }

    private sealed class FixedRuntimeIntrinsicTypeRootPlanner(RuntimeIntrinsicTypeRootPlan plan) :
        IRuntimeIntrinsicTypeRootPlanner
    {
        internal HashSet<CliTypeIdentity> ObservedTypes { get; } = [];

        public RuntimeIntrinsicTypeRootPlan Plan(
            IEnumerable<MethodInstanceModel> reachableMethods,
            IEnumerable<CliTypeIdentity> reachableTypes)
        {
            ObservedTypes.UnionWith(reachableTypes);
            return plan;
        }
    }

    private sealed class EmptyObjectArrayDelegateAdapterPlanner :
        IObjectArrayDelegateAdapterPlanner
    {
        public ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan> Plan(
            IEnumerable<MethodInstanceModel> reachableMethods) => [];
    }

    private sealed class FixedMemberExecutionPlanner(MemberExecutionPlan plan) : IMemberExecutionPlanner
    {
        public MemberExecutionPlan Plan(
            IEnumerable<MethodInstanceModel> reachableMethods,
            IEnumerable<MethodInstanceModel> methodDescriptors,
            IEnumerable<FieldInstanceModel> fieldDescriptors) =>
            plan;
    }

    private sealed class ConditionalRuntimeIntrinsicTypeRootPlanner(
        CliTypeIdentity trigger,
        CliTypeIdentity result) : IRuntimeIntrinsicTypeRootPlanner
    {
        public int Calls { get; private set; }
        public bool SawTrigger { get; private set; }

        public RuntimeIntrinsicTypeRootPlan Plan(
            IEnumerable<MethodInstanceModel> reachableMethods,
            IEnumerable<CliTypeIdentity> reachableTypes)
        {
            Calls++;
            SawTrigger |= reachableTypes.Contains(trigger);
            return SawTrigger ? new([result], []) : RuntimeIntrinsicTypeRootPlan.Empty;
        }
    }

    private sealed class ChangingPropertyAccessorResolver(
        IMetadataPropertyAccessorResolver inner,
        PropertyDefinitionModel definition,
        CliTypeIdentity declaringType,
        MethodDefinitionModel getter,
        MethodDefinitionModel setter) : IMetadataPropertyAccessorResolver
    {
        private int _calls;

        public PropertyInstanceModel? Resolve(MethodInstanceModel accessor)
        {
            var property = inner.Resolve(accessor);
            if (property is null || ++_calls <= 2)
            {
                return property;
            }
            return new PropertyInstanceModel(
                definition,
                declaringType,
                CliTypeIdentity.FromStackKind(CliValueKind.I8),
                property.IndexParameterTypes,
                new MethodInstanceModel(getter, declaringType, [], getter.Signature),
                new MethodInstanceModel(setter, declaringType, [], setter.Signature));
        }
    }

    private sealed class EmptyPropertyAccessorResolver :
        IMetadataPropertyAccessorResolver
    {
        public PropertyInstanceModel? Resolve(MethodInstanceModel accessor) => null;
    }

    private sealed class EmptyBaseTypeResolver : IBaseTypeResolver
    {
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => null;
    }

    private sealed class ReachabilityProgramRecorder : IReachableProgramBuilder
    {
        public ReachabilityLedgerSnapshot? Snapshot { get; private set; }

        public ReachableProgram Build(
            MethodDefinitionModel? entryPoint,
            ImmutableArray<ProgramExport> exports,
            ReachabilityLedgerSnapshot state,
            IDelegateTypeRecognizer delegateTypes,
            ITypeTestPlanner typeTestPlanner)
        {
            Snapshot = state;
            return new(
                entryPoint,
                [],
                [],
                [],
                [],
                [],
                exports,
                [],
                [],
                [],
                []);
        }
    }

    private sealed class NoIntrinsicAddressBodyPlanner : IIntrinsicAddressBodyPlanner
    {
        public ReachableMethodRequest? Plan(MethodInstanceModel method) => null;
    }
}
