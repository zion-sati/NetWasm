using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

// One closure's state: what the program reaches so far and the work still queued, with the operations that add to
// them. The closure builder's collaborators apply what they find through it; it lives for one Build, which owns it.
internal sealed class ReachabilitySession : IDisposable
{
    private readonly ReachabilityLedger _state;
    private readonly IDelegateTypeRecognizer _delegateTypes;
    private readonly IFieldRepository _fieldRepository;
    private readonly IFinalizerResolver _finalizerResolver;
    private readonly IReachabilityImportClassifier _importClassifier;
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers;
    private readonly IIntrinsicAddressBodyPlanner _intrinsicAddressBodies;
    private readonly IMethodInstanceResolver _methodInstances;
    private readonly IMethodRepository _methodRepository;
    private readonly IModuleInitializerResolver _moduleInitializers;
    private readonly INullableTypeResolver _nullableTypes;
    private readonly ISymbolFormatter _symbols;
    private readonly ITypeDefinitionResolver _typeDefinitions;
    private readonly ITypeFinder _typeFinder;
    private readonly ITypeIdentityResolver _typeIdentities;
    private readonly ITypeRepository _typeRepository;
    private readonly IGenericCycleSession _genericCycleSession;
    private readonly IDispatchCandidateIndex _dispatchCandidateIndex;
    private readonly IReadOnlySet<EntityKey> _outwardBoundaries;
    private readonly MethodDefinitionModel? _entryPoint;
    private readonly ImmutableDictionary<string, CilMethodBody>? _rewrittenBodies;
    // Bodies that replace methods cut at a generic cycle.
    private readonly Dictionary<string, CilMethodBody> _cycleBodies = new(StringComparer.Ordinal);

    internal ReachabilitySession(
        ReachabilityLedger state,
        ReachableMethodAnalysisSession methodAnalysis,
        IGenericCycleSession genericCycleSession,
        IDispatchCandidateIndex dispatchCandidateIndex,
        IReadOnlySet<EntityKey> outwardBoundaries,
        MethodDefinitionModel? entryPoint,
        ImmutableDictionary<string, CilMethodBody>? rewrittenBodies,
        IDelegateTypeRecognizer delegateTypes,
        IFieldRepository fieldRepository,
        IFinalizerResolver finalizerResolver,
        IReachabilityImportClassifier importClassifier,
        IMethodInitializationTriggerClassifier initializationTriggers,
        IIntrinsicAddressBodyPlanner intrinsicAddressBodies,
        IMethodInstanceResolver methodInstances,
        IMethodRepository methodRepository,
        IModuleInitializerResolver moduleInitializers,
        INullableTypeResolver nullableTypes,
        ISymbolFormatter symbols,
        ITypeDefinitionResolver typeDefinitions,
        ITypeFinder typeFinder,
        ITypeIdentityResolver typeIdentities,
        ITypeRepository typeRepository)
    {
        _state = state;
        MethodAnalysis = methodAnalysis;
        _genericCycleSession = genericCycleSession;
        _dispatchCandidateIndex = dispatchCandidateIndex;
        _outwardBoundaries = outwardBoundaries;
        _entryPoint = entryPoint;
        _rewrittenBodies = rewrittenBodies;
        _delegateTypes = delegateTypes;
        _fieldRepository = fieldRepository;
        _finalizerResolver = finalizerResolver;
        _importClassifier = importClassifier;
        _initializationTriggers = initializationTriggers;
        _intrinsicAddressBodies = intrinsicAddressBodies;
        _methodInstances = methodInstances;
        _methodRepository = methodRepository;
        _moduleInitializers = moduleInitializers;
        _nullableTypes = nullableTypes;
        _symbols = symbols;
        _typeDefinitions = typeDefinitions;
        _typeFinder = typeFinder;
        _typeIdentities = typeIdentities;
        _typeRepository = typeRepository;
    }

    internal ReachabilityLedger State => _state;

    // Only method-body dependencies seed runtime-typed array creation; derived array roots must not feed themselves
    // on the next closure pass.
    internal HashSet<CliTypeIdentity> ArrayElementSeeds { get; } = [];

    // Types whose handles method bodies name; RuntimeHelpers.RunClassConstructor compiles their initializers.
    internal HashSet<CliTypeIdentity> TypeHandleSeeds { get; } = [];

    // The dependency path of the code being applied, recorded for what it reaches.
    internal ImmutableStack<CompilerDependencyStep> CurrentDependencyPath { get; set; } =
        ImmutableStack<CompilerDependencyStep>.Empty;

    // The receiver whose dispatch is being resolved: the method that created the receiver led to its targets as much
    // as the call site did.
    internal CliTypeIdentity? CurrentReceiver { get; set; }

    internal Queue<ReachableMethodRequest> IntrinsicAddressBodies { get; } = new();

    // Methods are classified and their analysis started when they are discovered; the closure still applies them,
    // and any classification failure, in discovery order.
    internal ReachableMethodAnalysisSession MethodAnalysis { get; }

    internal Dictionary<string, DiscoveredMethod> DiscoveredRequests { get; } = new(StringComparer.Ordinal);

    public void Dispose() => MethodAnalysis.Dispose();

    internal void Enqueue(MethodDefinitionModel method)
    {
        EnqueueInstance(DirectInstance(method));
    }

    internal void VisitInvokedRoot(MethodDefinitionModel method, string role)
    {
        RejectOpenRoot(method, role);
        VisitMethod(CilOperation.Call, DirectInstance(method));
    }

    internal (
        int TypeCount,
        int RuntimeTypeDefinitionCount,
        int ConstructedTypeCount,
        int AllocatedTypeCount,
        int MethodInstanceCount,
        int MethodDescriptorCount,
        int FieldDescriptorCount,
        int PropertyDescriptorCount,
        int ObjectArrayDelegateAdapterCount,
        int MemberExecutionMethodCount,
        int MemberExecutionFieldCount,
        int MemberExecutionDispatchTargetCount,
        int DynamicInvokeMethodCount,
        bool HasMemberExecutionDemand,
        bool HasDynamicInvokeDemand,
        bool RequiresTypeFacts,
        bool RequiresGenericArguments,
        bool RequiresDeclaringTypes,
        bool RequiresGenericDefinitions,
        bool RequiresAssemblies,
        bool RequiresElementTypes,
        bool RequiresTypeAttributes,
        bool RequiresInterfaces,
        bool RequiresMemberNames)
        CaptureClosureStamp() => (
        _state.Types.Count,
        _state.RuntimeTypeDefinitions.Count,
        _state.ConstructedTypes.Count,
        _state.AllocatedTypes.Count,
        _state.MethodInstances.Count,
        _state.MethodDescriptors.Count,
        _state.FieldDescriptors.Count,
        _state.PropertyDescriptors.Count,
        _state.ObjectArrayDelegateAdapters.Count,
        _state.MemberExecution.Methods.Count,
        _state.MemberExecution.Fields.Count,
        _state.MemberExecution.Methods.Values.Sum(
            execution => execution.Targets.Length),
        _state.MemberExecution.DelegateInvocation?.Methods.Count ?? 0,
        _state.MemberExecution.UnsupportedTarget is not null,
        _state.MemberExecution.DelegateInvocation is not null,
        _state.RequiresTypeFacts,
        _state.RequiresGenericArguments,
        _state.RequiresDeclaringTypes,
        _state.RequiresGenericDefinitions,
        _state.RequiresAssemblies,
        _state.RequiresElementTypes,
        _state.RequiresTypeAttributes,
        _state.RequiresInterfaces,
        _state.RequiresMemberNames);

    private void EnsureModuleInitialized(AssemblyIdentity assembly)
    {
        if (!_moduleInitializers.TryResolve(assembly, out var initializer) ||
            !_state.ModuleInitializers.Add(initializer.Key))
        {
            return;
        }

        _state.StaticInitializers.Add(initializer.Key);
        Enqueue(initializer);
    }

    internal void AddCallable(MethodInstanceModel method)
    {
        if (_state.CallableMethods.TryAdd(method.CanonicalName, method) &&
            _intrinsicAddressBodies.Plan(method) is { } request)
        {
            IntrinsicAddressBodies.Enqueue(request);
            MethodAnalysis.Prefetcher.Prefetch(request);
        }
    }

    internal void EnqueueInstance(MethodInstanceModel method)
    {
        EnsureModuleInitialized(method.Definition.Key.Assembly);
        if (method.Definition.GenericArity != method.MethodArguments.Length ||
            method.DeclaringType.ContainsGenericParameters ||
            method.MethodArguments.Any(argument => argument.ContainsGenericParameters) ||
            method.Signature.ReturnSignatureType.ContainsGenericParameters ||
            method.Signature.ParameterSignatureTypes.Any(
                parameter => parameter.ContainsGenericParameters))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"reachable method '{method.CanonicalName}' contains an unresolved " +
                "generic parameter and requires a closed AOT specialization"));
        }
        if (method.IsConstructed)
        {
            AddConstructedIdentity(method.CanonicalName);
        }
        if (_state.Discovered.Add(method.CanonicalName))
        {
            var cut = _genericCycleSession.Admit(method, GenericCycleParents());
            if (cut is not null)
            {
                _cycleBodies.Add(method.CanonicalName, cut);
            }
            _state.MethodDependencyPaths.Add(method.CanonicalName, CurrentDependencyPath);
            _state.Pending.Enqueue(method);
            DiscoveredRequests[method.CanonicalName] = Discover(method);
        }
    }

    // The methods that led to the one being discovered: the method whose code is being applied and, for a dispatch
    // target, the method that first named the receiver's type.
    private IEnumerable<string> GenericCycleParents()
    {
        if (NearestMethod(CurrentDependencyPath) is { } caller)
        {
            yield return caller;
        }
        if (CurrentReceiver is not null &&
            _state.TypeDependencyPaths.TryGetValue(CurrentReceiver.CanonicalName, out var receiverPath) &&
            NearestMethod(receiverPath) is { } creator)
        {
            yield return creator;
        }

        static string? NearestMethod(ImmutableStack<CompilerDependencyStep> path)
        {
            foreach (var step in path)
            {
                if (step.Kind == CompilerDependencyKind.MethodDependency &&
                    step.Source.InstanceIdentity is { } identity)
                {
                    return identity;
                }
            }
            return null;
        }
    }

    // Classifies a discovered method and, unless an import handles it, starts its analysis.
    // Classification reads only the method, its dependency path (recorded as it is queued)
    // and the closure's fixed boundaries.
    private DiscoveredMethod Discover(MethodInstanceModel method)
    {
        ReachabilityImportAnalysis import;
        try
        {
            import = _importClassifier.Classify(new ReachabilityImportRequest(
                method,
                _outwardBoundaries.Contains(method.Definition.Key),
                _entryPoint?.Key == method.Definition.Key)
            {
                DependencyPath = _state.MethodDependencyPaths[method.CanonicalName],
            });
        }
        catch (Exception exception)
        {
            return new(null, ExceptionDispatchInfo.Capture(exception), null);
        }
        if (import.IsHandled)
        {
            return new(import, null, null);
        }
        var request = new ReachableMethodRequest(method)
        {
            DependencyPath = _state.MethodDependencyPaths[method.CanonicalName],
            RewrittenBody = _cycleBodies.GetValueOrDefault(method.CanonicalName) ??
                _rewrittenBodies?.GetValueOrDefault(method.CanonicalName),
        };
        MethodAnalysis.Prefetcher.Prefetch(request);
        return new(import, null, request);
    }

    internal void AddAllocatedType(CliTypeIdentity type)
    {
        if (!_state.AllocatedTypes.Add(type))
        {
            return;
        }

        EnqueueDispatches(_dispatchCandidateIndex.Index(
            new DispatchReceiverCandidate(type)));
    }

    internal void AddDispatch(string key, DispatchDeclaration declaration)
    {
        var candidates = _dispatchCandidateIndex.Index(
            new DispatchDeclarationCandidate(key, declaration));
        if (!_state.DispatchDeclarations.TryAdd(key, declaration))
        {
            return;
        }

        _state.DispatchDependencyPaths.Add(key, CurrentDependencyPath);
        EnqueueDispatches(candidates);
    }

    private void EnqueueDispatches(ImmutableArray<DispatchCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            _state.PendingDispatches.Enqueue((
                candidate.DispatchKey,
                candidate.Declaration,
                candidate.Receiver));
        }
    }

    internal void AddConstructedType(
        CliTypeIdentity type,
        bool retainElementModifierIdentity = false)
    {
        _state.TypeDependencyPaths.TryAdd(type.CanonicalName, CurrentDependencyPath);
        var isElementModifier = type.Shape is
            CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer;
        if (type.Shape is CliTypeShape.GenericInstantiation or
                CliTypeShape.SzArray or CliTypeShape.Array ||
            isElementModifier && retainElementModifierIdentity)
        {
            AddConstructedIdentity(type.CanonicalName);
            _state.ConstructedTypes.Add(type);
        }
        if (isElementModifier)
        {
            AddConstructedType(type.ElementType!);
        }
        foreach (var argument in type.TypeArguments)
        {
            AddConstructedType(argument);
        }
    }

    private void AddConstructedIdentity(string identity)
    {
        _state.ConstructedIdentities.Add(identity);
    }

    internal void AddRuntimeType(CliTypeIdentity type)
    {
        _state.TypeDependencyPaths.TryAdd(type.CanonicalName, CurrentDependencyPath);
        if (type.Shape is CliTypeShape.Named or
            CliTypeShape.GenericInstantiation)
        {
            try
            {
                var definition = _typeDefinitions.ResolveTypeIdentity(type);
                _state.Types.Add(definition.Key);
                if (type.Shape == CliTypeShape.Named && definition.GenericArity > 0)
                {
                    _state.RuntimeTypeDefinitions.Add(definition.Key);
                }
            }
            catch (CompilerException exception)
            {
                throw new CompilerException(exception.Diagnostic with
                {
                    Message = $"runtime type '{type.CanonicalName}' could not be resolved: " +
                        exception.Diagnostic.Message,
                }, exception);
            }
            return;
        }
        if (PrimitiveTypeNames.TryGetFullName(type.CanonicalName, out var primitive))
        {
            _state.Types.Add(_typeFinder.FindType(primitive).Key);
        }
    }

    internal void VisitEntity(CilOperation operation, EntityKey key)
    {
        if (operation == CilOperation.NewArray)
        {
            _state.Types.Add(key);
            _state.Types.Add(_typeFinder.FindType("System.Array").Key);
            return;
        }
        if (operation is CilOperation.CastClass or CilOperation.IsInstance)
        {
            _state.Types.Add(key);
            AddConstructedType(_typeIdentities.GetTypeIdentity(key));
            return;
        }
        if (operation is CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject)
        {
            var target = _methodRepository.GetMethod(key);
            VisitMethod(operation, DirectInstance(target));
            return;
        }
        if (operation is CilOperation.LoadField or CilOperation.LoadFieldAddress or
            CilOperation.StoreField or CilOperation.LoadStaticField or
            CilOperation.LoadStaticFieldAddress or CilOperation.StoreStaticField)
        {
            var field = _fieldRepository.GetField(key);
            VisitField(new FieldInstanceModel(
                field,
                _typeIdentities.GetTypeIdentity(field.DeclaringType),
                field.SignatureType));
        }
    }

    internal bool IsDelegateInvoke(MethodInstanceModel method) =>
        method.Definition.Name == "Invoke" && _delegateTypes.Recognize(method.DeclaringType);

    internal void VisitMethod(CilOperation operation, MethodInstanceModel target)
    {
        EnqueueInstance(target);
        if (_initializationTriggers.Classify(target.Definition))
        {
            EnsureTypeInitialized(target.Definition.DeclaringType, target.DeclaringType);
        }
        _state.Types.Add(target.Definition.DeclaringType);
        AddConstructedType(target.DeclaringType);
        foreach (var argument in target.MethodArguments)
        {
            AddConstructedType(argument);
        }
        // Closed method signatures can introduce constructed return and
        // parameter types without an explicit type operand in the CIL
        // body (for example, TEnum[] from Enum.GetValues<TEnum>()). Keep
        // their layouts available to intrinsic emitters and array casts.
        AddConstructedType(target.Signature.ReturnSignatureType);
        foreach (var parameter in target.Signature.ParameterSignatureTypes)
        {
            AddConstructedType(parameter);
        }
        if (operation != CilOperation.NewObject)
        {
            return;
        }

        var finalizer = _finalizerResolver.Resolve(target.DeclaringType);
        if (finalizer is not null)
        {
            _state.Finalizers[target.DeclaringType] = finalizer;
            EnqueueInstance(finalizer);
        }
    }

    internal void VisitField(FieldInstanceModel field)
    {
        if (field.IsConstructed)
        {
            _state.ConstructedFields.TryAdd(field.CanonicalName, field);
        }
        _state.Fields.Add(field.Definition.Key);
        _state.Types.Add(field.Definition.DeclaringType);
        AddConstructedType(field.DeclaringType);
        AddConstructedType(field.FieldType);
        if (!field.Definition.IsStatic)
        {
            return;
        }

        EnsureModuleInitialized(field.Definition.Key.Assembly);
        EnsureTypeInitialized(field.Definition.DeclaringType, field.DeclaringType);
    }

    internal void AddMethodDescriptor(MethodInstanceModel method)
    {
        if (!_state.MethodDescriptors.TryAdd(method.CanonicalName, method))
        {
            return;
        }

        AddDescriptorImplementationType(method.Definition.Name is ".ctor" or ".cctor"
            ? "System.Reflection.RuntimeConstructorInfo"
            : "System.Reflection.RuntimeMethodInfo");
        AddDescriptorType(method.DeclaringType);
        AddDescriptorType(method.Signature.ReturnSignatureType);
        foreach (var parameter in method.Signature.ParameterSignatureTypes)
        {
            AddDescriptorType(parameter);
        }
        foreach (var argument in method.MethodArguments)
        {
            AddDescriptorType(argument);
        }
    }

    internal void AddFieldDescriptor(FieldInstanceModel field)
    {
        if (!_state.FieldDescriptors.TryAdd(field.CanonicalName, field))
        {
            return;
        }

        AddDescriptorImplementationType("System.Reflection.RuntimeFieldInfo");
        AddDescriptorType(field.DeclaringType);
        AddDescriptorType(field.FieldType);
    }

    internal void AddPropertyDescriptor(PropertyInstanceModel property)
    {
        if (_state.PropertyDescriptors.TryGetValue(
                property.CanonicalName,
                out var existing))
        {
            if (!existing.HasEquivalentDescriptorFacts(property))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    $"member descriptor '{property.CanonicalName}' has contradictory properties"));
            }
            return;
        }
        _state.PropertyDescriptors.Add(property.CanonicalName, property);

        AddDescriptorImplementationType("System.Reflection.RuntimePropertyInfo");
        AddDescriptorType(property.DeclaringType);
        AddDescriptorType(property.PropertyType);
        foreach (var parameter in property.IndexParameterTypes)
        {
            AddDescriptorType(parameter);
        }
    }

    private void AddDescriptorImplementationType(string fullName)
    {
        var definition = _typeFinder.FindType(fullName);
        _state.Types.Add(definition.Key);
        AddAllocatedType(_typeIdentities.GetTypeIdentity(definition.Key));
    }

    internal void AddDescriptorType(CliTypeIdentity type)
    {
        AddRuntimeType(type);
        AddConstructedType(type, retainElementModifierIdentity: true);
    }

    internal void AddMemberExecutionValue(CliTypeIdentity type)
    {
        if (type.StackKind is not (CliValueKind.ManagedReference or CliValueKind.Void))
        {
            AddAllocatedType(_nullableTypes.Resolve(type) ?? type);
        }
    }

    internal void EnsureTypeInitialized(EntityKey definition, CliTypeIdentity declaringType)
    {
        var typeDefinition = _typeRepository.GetTypeDefinition(definition);
        // Only a generic type's instantiations are initialized; RunClassConstructor of the definition itself
        // (typeof(List<>).TypeHandle) does nothing, as on desktop .NET.
        if (typeDefinition.GenericArity > 0 && declaringType.Shape != CliTypeShape.GenericInstantiation)
        {
            return;
        }
        var initializer = typeDefinition
            .Methods
            .Select(_methodRepository.GetMethod)
            .SingleOrDefault(candidate => candidate.Name == ".cctor");
        if (initializer is null)
        {
            return;
        }
        if (declaringType.Shape == CliTypeShape.GenericInstantiation)
        {
            var constructedInitializer = ConstructedInstance(
                initializer,
                declaringType.TypeArguments);
            _state.ConstructedStaticInitializers.Add(constructedInitializer.CanonicalName);
            EnqueueInstance(constructedInitializer);
        }
        else
        {
            _state.StaticInitializers.Add(initializer.Key);
            Enqueue(initializer);
        }
    }

    internal MethodInstanceModel DirectInstance(MethodDefinitionModel method) =>
        _methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            _symbols.Format(method),
            0);

    private MethodInstanceModel ConstructedInstance(
        MethodDefinitionModel method,
        ImmutableArray<CliTypeIdentity> typeArguments) =>
        _methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            _symbols.Format(method),
            0,
            new CliGenericContext(typeArguments, []));

    // managed.throw calls the kind's CoreLib getter for a failed check's
    // exception.
    internal void AddImplicit(ManagedExceptionKind kind, string typeName)
    {
        if (_state.ImplicitExceptions.Add(kind))
        {
            var type = _typeFinder.FindType(typeName);
            _state.Types.Add(type.Key);
            AddAllocatedType(_typeIdentities.GetTypeIdentity(type.Key));
            Enqueue(_typeFinder.FindType(RuntimeExceptionGetters.TypeName).Methods
                .Select(_methodRepository.GetMethod)
                .Single(method => method.Name == RuntimeExceptionGetters.MethodName(kind)));
        }
    }

    internal void RejectOpenRoot(MethodDefinitionModel method, string role)
    {
        var declaringType = _typeRepository.GetTypeDefinition(method.DeclaringType);
        if (method.GenericArity == 0 && declaringType.GenericArity == 0)
        {
            return;
        }
        throw new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"open generic {role} '{_symbols.Format(method)}' " +
            "requires a closed AOT specialization"));
    }

    internal sealed record DiscoveredMethod(
        ReachabilityImportAnalysis? Import,
        ExceptionDispatchInfo? Failure,
        ReachableMethodRequest? Request);
}
