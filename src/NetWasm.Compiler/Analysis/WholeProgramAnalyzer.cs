using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis.ManagedCallSites;

namespace NetWasm.Compiler.Analysis;

// Builder for the immutable reachable-program closure. Composition happens in
// WholeProgramAnalyzerFactory; this type owns only one Build action.
internal sealed class ReachabilityClosureBuilder(
    ITypeRepository typeRepository,
    IFieldRepository fieldRepository,
    IMethodRepository methodRepository,
    ITypeFinder typeFinder,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver typeIdentities,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    IDispatchTargetResolver dispatchTargets,
    IDelegateTypeRecognizer delegateTypes,
    IReachabilityImportClassifier importClassifier,
    IReachableMethodBatchAnalyzer methodBatchAnalyzer,
    ITypeTestPlanner typeTestPlanner,
    IReachableProgramBuilder programBuilder,
    IReachabilityLedgerFactory ledgers,
    IManagedCallSiteLedgerWriter managedCallSites,
    IRuntimeIntrinsicTypeRootPlanner runtimeIntrinsicTypeRoots,
    IReachabilityClosureObserver closureObserver,
    IDispatchCandidateIndexFactory dispatchCandidateIndexes,
    IModuleInitializerResolver moduleInitializers,
    IModuleInitializerOrderer moduleInitializerOrderer) : IReachabilityClosureBuilder
{
    private readonly IReachabilityClosureObserver _closureObserver =
        closureObserver ?? throw new ArgumentNullException(nameof(closureObserver));
    private readonly IDispatchCandidateIndexFactory _dispatchCandidateIndexes =
        dispatchCandidateIndexes ?? throw new ArgumentNullException(nameof(dispatchCandidateIndexes));
    private readonly ITypeRepository _typeRepository = typeRepository ?? throw new ArgumentNullException(nameof(typeRepository));
    private readonly IFieldRepository _fieldRepository = fieldRepository ?? throw new ArgumentNullException(nameof(fieldRepository));
    private readonly IMethodRepository _methodRepository = methodRepository ?? throw new ArgumentNullException(nameof(methodRepository));
    private readonly ITypeFinder _typeFinder = typeFinder ?? throw new ArgumentNullException(nameof(typeFinder));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ?? throw new ArgumentNullException(nameof(typeIdentities));
    private readonly IMethodInstanceResolver _methodInstances = methodInstances ?? throw new ArgumentNullException(nameof(methodInstances));
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
    private readonly IDispatchTargetResolver _dispatchTargets = dispatchTargets ?? throw new ArgumentNullException(nameof(dispatchTargets));
    private readonly IDelegateTypeRecognizer _delegateTypes = delegateTypes ?? throw new ArgumentNullException(nameof(delegateTypes));
    private readonly IReachabilityImportClassifier _importClassifier = importClassifier ?? throw new ArgumentNullException(nameof(importClassifier));
    private readonly IReachableMethodBatchAnalyzer _methodBatchAnalyzer = methodBatchAnalyzer ?? throw new ArgumentNullException(nameof(methodBatchAnalyzer));
    private readonly ITypeTestPlanner _typeTestPlanner = typeTestPlanner ?? throw new ArgumentNullException(nameof(typeTestPlanner));
    private readonly IReachableProgramBuilder _programBuilder = programBuilder ?? throw new ArgumentNullException(nameof(programBuilder));
    private readonly IReachabilityLedgerFactory _ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
    private readonly IManagedCallSiteLedgerWriter _managedCallSites = managedCallSites ?? throw new ArgumentNullException(nameof(managedCallSites));
    private readonly IRuntimeIntrinsicTypeRootPlanner _runtimeIntrinsicTypeRoots =
        runtimeIntrinsicTypeRoots ?? throw new ArgumentNullException(nameof(runtimeIntrinsicTypeRoots));
    private readonly IModuleInitializerResolver _moduleInitializers =
        moduleInitializers ?? throw new ArgumentNullException(nameof(moduleInitializers));
    private readonly IModuleInitializerOrderer _moduleInitializerOrderer =
        moduleInitializerOrderer ?? throw new ArgumentNullException(nameof(moduleInitializerOrderer));

    public ReachableProgram Build(
        MethodDefinitionModel entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(requestedExports);
        roots ??= ReachabilityRoots.Empty;
        var exports = requestedExports.ToArray();
        var outwardBoundaries = exports
            .Select(export => export.Method)
            .Append(entryPoint.Key)
            .ToHashSet();
        var state = _ledgers.Create();
        var methodInstances = state.MethodInstances;
        var constructedTypes = state.ConstructedTypes;
        var constructedFields = state.ConstructedFields;
        var types = state.Types;
        var fields = state.Fields;
        var staticInitializers = state.StaticInitializers;
        var moduleInitializers = state.ModuleInitializers;
        var constructedStaticInitializers = state.ConstructedStaticInitializers;
        var finalizers = state.Finalizers;
        var implicitExceptions = state.ImplicitExceptions;
        var pending = state.Pending;
        var pendingDispatches = state.PendingDispatches;
        var discovered = state.Discovered;
        var constructedIdentities = state.ConstructedIdentities;
        var allocatedTypes = state.AllocatedTypes;
        var dispatchDeclarations = state.DispatchDeclarations;
        var dispatchTargets = state.DispatchTargets;
        var callableMethods = state.CallableMethods;
        var dispatchCandidates = _dispatchCandidateIndexes.Create();

        RejectOpenRoot(entryPoint, "entry point");
        foreach (var type in roots.Types)
        {
            types.Add(type);
        }
        foreach (var field in roots.Fields)
        {
            fields.Add(field);
        }
        foreach (var method in roots.Methods)
        {
            var rootedMethod = _methodRepository.GetMethod(method);
            RejectOpenRoot(rootedMethod, "method root");
            Enqueue(rootedMethod);
        }
        Enqueue(entryPoint);
        foreach (var export in exports)
        {
            var exportedMethod = _methodRepository.GetMethod(export.Method);
            RejectOpenRoot(exportedMethod, "export");
            Enqueue(exportedMethod);
        }

        while (pending.Count != 0 || pendingDispatches.Count != 0)
        {
            var iterationStarted = Stopwatch.GetTimestamp();
            var methodPublicationTicks = 0L;
            var dispatchCount = 0;
            var resolvedDispatchCount = 0;
            var methodRequests = ImmutableArray.CreateBuilder<ReachableMethodRequest>(pending.Count);
            while (pending.TryDequeue(out var methodInstance))
            {
                var method = methodInstance.Definition;
                methodInstances.Add(methodInstance.CanonicalName, methodInstance);
                types.Add(method.DeclaringType);
                var import = _importClassifier.Classify(new ReachabilityImportRequest(
                    methodInstance,
                    outwardBoundaries.Contains(method.Key)));
                ApplyImport(import);
                if (import.IsHandled)
                {
                    continue;
                }
                methodRequests.Add(new ReachableMethodRequest(methodInstance));
            }
            foreach (var analysis in _methodBatchAnalyzer.Analyze(methodRequests.ToImmutable()))
            {
                var publicationStarted = Stopwatch.GetTimestamp();
                ApplyMethod(analysis);
                _managedCallSites.Write(state, analysis.Instructions.CallSites);
                methodPublicationTicks += Stopwatch.GetTimestamp() - publicationStarted;
            }

            var dispatchStarted = Stopwatch.GetTimestamp();
            while (pendingDispatches.TryDequeue(out var work))
            {
                dispatchCount++;
                var target = _dispatchTargets.Resolve(
                    work.Declaration.Declaration,
                    work.Receiver);
                if (target is null)
                {
                    continue;
                }

                resolvedDispatchCount++;

                if (!dispatchTargets.TryGetValue(work.DispatchKey, out var targets))
                {
                    targets = new Dictionary<string, DispatchTargetModel>(
                        StringComparer.Ordinal);
                    dispatchTargets.Add(work.DispatchKey, targets);
                }
                targets.TryAdd(target.ReceiverType.CanonicalName, target);
                EnqueueInstance(target.Method);
                if (work.Declaration.Operation == CilOperation.LoadVirtualFunction)
                {
                    callableMethods.TryAdd(target.Method.CanonicalName, target.Method);
                }
            }
            var dispatchPublicationTicks = Stopwatch.GetTimestamp() - dispatchStarted;

            _closureObserver.Observe(new ReachabilityClosureObservation(
                methodRequests.Count,
                Stopwatch.GetElapsedTime(0, methodPublicationTicks),
                dispatchCount,
                resolvedDispatchCount,
                Stopwatch.GetElapsedTime(0, dispatchPublicationTicks),
                Stopwatch.GetElapsedTime(iterationStarted)));
        }

        if (allocatedTypes.Any(_delegateTypes.Recognize))
        {
            AddImplicit(ManagedExceptionKind.Argument, "System.ArgumentException");
            AddImplicit(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException");
        }
        foreach (var type in _runtimeIntrinsicTypeRoots.Plan(
                     methodInstances.Values,
                     constructedTypes))
        {
            AddRuntimeType(type);
        }
        var program = _programBuilder.Build(
            entryPoint,
            [.. exports],
            ReachabilityLedgerSnapshot.From(state),
            _delegateTypes,
            _typeTestPlanner);
        return program with
        {
            ModuleInitializers = _moduleInitializerOrderer.Order(program.ModuleInitializers),
        };

        void Enqueue(MethodDefinitionModel method)
        {
            EnqueueInstance(DirectInstance(method));
        }

        void EnsureModuleInitialized(AssemblyIdentity assembly)
        {
            if (!_moduleInitializers.TryResolve(assembly, out var initializer) ||
                !moduleInitializers.Add(initializer.Key))
            {
                return;
            }

            staticInitializers.Add(initializer.Key);
            Enqueue(initializer);
        }

        void ApplyMethod(ReachableMethodAnalysis analysis)
        {
            foreach (var requirement in analysis.Exceptions)
            {
                AddImplicit(requirement.Kind, requirement.TypeName);
            }
            foreach (var catchType in analysis.CatchTypes)
            {
                types.Add(catchType);
            }
            if (analysis.Method.IsConstructed)
            {
                state.ConstructedMethods.Add(analysis.Method.CanonicalName, analysis.Body);
            }
            else
            {
                state.Methods.Add(analysis.Method.Definition.Key, analysis.Body);
            }
            ApplyInstructions(analysis.Instructions);
        }

        void ApplyInstructions(ReachabilityInstructionAnalysis analysis)
        {
            foreach (var type in analysis.RuntimeTypes)
            {
                AddRuntimeType(type);
            }
            foreach (var type in analysis.ConstructedTypes)
            {
                AddConstructedType(type);
            }
            foreach (var type in analysis.AllocatedTypes)
            {
                AddAllocatedType(type);
            }
            foreach (var type in analysis.Types)
            {
                types.Add(type);
            }
            foreach (var literal in analysis.Strings)
            {
                state.Strings.Add(literal);
            }
            foreach (var method in analysis.Methods)
            {
                VisitMethod(method.Operation, method.Method);
            }
            foreach (var entity in analysis.Entities)
            {
                VisitEntity(entity.Operation, entity.Entity);
            }
            foreach (var field in analysis.Fields)
            {
                VisitField(field);
            }
            foreach (var dispatch in analysis.Dispatches)
            {
                AddDispatch(dispatch.Key, dispatch.Declaration);
            }
            foreach (var callable in analysis.CallableMethods)
            {
                state.CallableMethods.TryAdd(callable.CanonicalName, callable);
            }
        }

        void ApplyImport(ReachabilityImportAnalysis analysis)
        {
            foreach (var type in analysis.ConstructedTypes)
            {
                AddConstructedType(type);
            }
            foreach (var type in analysis.AllocatedTypes)
            {
                AddAllocatedType(type);
            }
            foreach (var type in analysis.Types)
            {
                types.Add(type);
            }
            foreach (var field in analysis.Fields)
            {
                VisitField(field);
            }
            foreach (var method in analysis.EnqueuedMethods)
            {
                EnqueueInstance(method);
            }
            foreach (var requirement in analysis.Exceptions)
            {
                AddImplicit(requirement.Kind, requirement.TypeName);
            }
            if (analysis.JavaScriptImport is not null)
            {
                state.JavaScriptImports.TryAdd(
                    analysis.JavaScriptImport.Key,
                    analysis.JavaScriptImport);
            }
            if (analysis.WitImport is not null)
            {
                state.WitImports.TryAdd(analysis.WitImport.Key, analysis.WitImport);
            }
            foreach (var callback in analysis.HostCallbacks)
            {
                state.HostCallbacks.Add(callback);
            }
            if (analysis.JavaScriptAsyncBinding is not null)
            {
                state.JavaScriptAsyncBindings.TryAdd(
                    analysis.JavaScriptAsyncBinding.Method,
                    analysis.JavaScriptAsyncBinding);
            }
        }

        void EnqueueInstance(MethodInstanceModel method)
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
            if (discovered.Add(method.CanonicalName))
            {
                pending.Enqueue(method);
            }
        }

        void AddAllocatedType(CliTypeIdentity type)
        {
            if (!allocatedTypes.Add(type))
            {
                return;
            }

            EnqueueDispatches(dispatchCandidates.Index(
                new DispatchReceiverCandidate(type)));
        }

        void AddDispatch(string key, DispatchDeclaration declaration)
        {
            var candidates = dispatchCandidates.Index(
                new DispatchDeclarationCandidate(key, declaration));
            if (!dispatchDeclarations.TryAdd(key, declaration))
            {
                return;
            }

            EnqueueDispatches(candidates);
        }

        void EnqueueDispatches(ImmutableArray<DispatchCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                pendingDispatches.Enqueue((
                    candidate.DispatchKey,
                    candidate.Declaration,
                    candidate.Receiver));
            }
        }

        void AddConstructedType(CliTypeIdentity type)
        {
            if (type.Shape is CliTypeShape.GenericInstantiation or
                CliTypeShape.SzArray or CliTypeShape.Array)
            {
                AddConstructedIdentity(type.CanonicalName);
                constructedTypes.Add(type);
            }
            foreach (var argument in type.TypeArguments)
            {
                AddConstructedType(argument);
            }
        }

        void AddConstructedIdentity(string identity)
        {
            constructedIdentities.Add(identity);
        }

        void AddRuntimeType(CliTypeIdentity type)
        {
            if (type.Shape is CliTypeShape.Named or
                CliTypeShape.GenericInstantiation)
            {
                try
                {
                    var definition = _typeDefinitions.ResolveTypeIdentity(type);
                    types.Add(definition.Key);
                    if (type.Shape == CliTypeShape.Named && definition.GenericArity > 0)
                    {
                        state.RuntimeTypeDefinitions.Add(definition.Key);
                    }
                }
                catch (CompilerException exception)
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        exception.Diagnostic.Code,
                        $"runtime type '{type.CanonicalName}' could not be resolved: " +
                        exception.Diagnostic.Message));
                }
                return;
            }
            var primitive = type.CanonicalName switch
            {
                "primitive:void" => "System.Void",
                "primitive:bool" => "System.Boolean",
                "primitive:char" => "System.Char",
                "primitive:i1" => "System.SByte",
                "primitive:u1" => "System.Byte",
                "primitive:i2" => "System.Int16",
                "primitive:u2" => "System.UInt16",
                "primitive:i4" => "System.Int32",
                "primitive:u4" => "System.UInt32",
                "primitive:i8" => "System.Int64",
                "primitive:u8" => "System.UInt64",
                "primitive:f4" => "System.Single",
                "primitive:f8" => "System.Double",
                "primitive:nativeint" => "System.IntPtr",
                "primitive:nativeuint" => "System.UIntPtr",
                "primitive:string" => "System.String",
                "primitive:object" => "System.Object",
                _ => null,
            };
            if (primitive is not null)
            {
                types.Add(_typeFinder.FindType(primitive).Key);
            }
        }

        void VisitEntity(CilOperation operation, EntityKey key)
        {
            if (operation == CilOperation.NewArray)
            {
                types.Add(key);
                types.Add(_typeFinder.FindType("System.Array").Key);
                return;
            }
            if (operation is CilOperation.CastClass or CilOperation.IsInstance)
            {
                types.Add(key);
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

        void VisitMethod(CilOperation operation, MethodInstanceModel target)
        {
            EnqueueInstance(target);
            if (target.Definition.IsStatic && target.Definition.Name != ".cctor" &&
                !_typeRepository.GetTypeDefinition(target.Definition.DeclaringType).IsBeforeFieldInit)
            {
                EnsureTypeInitialized(target.Definition.DeclaringType, target.DeclaringType);
            }
            types.Add(target.Definition.DeclaringType);
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

            var finalizer = _typeRepository
                .GetTypeDefinition(target.Definition.DeclaringType)
                .Methods
                .Select(_methodRepository.GetMethod)
                .SingleOrDefault(candidate =>
                    candidate.Name == "Finalize" &&
                    !candidate.IsStatic &&
                    candidate.Signature.ReturnType == CliValueKind.Void &&
                    candidate.Signature.ParameterTypes.IsEmpty);
            if (finalizer is not null &&
                _symbols.Format(target.Definition.DeclaringType) != "System.Object")
            {
                finalizers[target.Definition.DeclaringType] = finalizer.Key;
                Enqueue(finalizer);
            }
        }

        void VisitField(FieldInstanceModel field)
        {
            if (field.IsConstructed)
            {
                constructedFields.TryAdd(field.CanonicalName, field);
            }
            fields.Add(field.Definition.Key);
            types.Add(field.Definition.DeclaringType);
            AddConstructedType(field.DeclaringType);
            AddConstructedType(field.FieldType);
            if (!field.Definition.IsStatic)
            {
                return;
            }

            EnsureModuleInitialized(field.Definition.Key.Assembly);
            EnsureTypeInitialized(field.Definition.DeclaringType, field.DeclaringType);
        }

        void EnsureTypeInitialized(EntityKey definition, CliTypeIdentity declaringType)
        {
            var initializer = _typeRepository
                .GetTypeDefinition(definition)
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
                constructedStaticInitializers.Add(constructedInitializer.CanonicalName);
                EnqueueInstance(constructedInitializer);
            }
            else
            {
                staticInitializers.Add(initializer.Key);
                Enqueue(initializer);
            }
        }

        MethodInstanceModel DirectInstance(MethodDefinitionModel method) =>
            _methodInstances.ResolveMethodInstance(
                method.Key.Assembly,
                method.Key.MetadataToken,
                _symbols.Format(method),
                0);

        MethodInstanceModel ConstructedInstance(
            MethodDefinitionModel method,
            ImmutableArray<CliTypeIdentity> typeArguments) =>
            _methodInstances.ResolveMethodInstance(
                method.Key.Assembly,
                method.Key.MetadataToken,
                _symbols.Format(method),
                0,
                new CliGenericContext(typeArguments, []));


        void AddImplicit(ManagedExceptionKind kind, string typeName)
        {
            if (implicitExceptions.Add(kind))
            {
                var type = _typeFinder.FindType(typeName);
                types.Add(type.Key);
                AddAllocatedType(_typeIdentities.GetTypeIdentity(type.Key));
            }
        }

        void RejectOpenRoot(MethodDefinitionModel method, string role)
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

    }
}

// Facade for the reachability closure builder used by the compiler pipeline.
internal sealed class WholeProgramAnalyzer(
    IReachabilityClosureBuilder closureBuilder) : IWholeProgramAnalyzer
{
    private readonly IReachabilityClosureBuilder _closureBuilder =
        closureBuilder ?? throw new ArgumentNullException(nameof(closureBuilder));

    public ReachableProgram Analyze(
        MethodDefinitionModel entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null) =>
        _closureBuilder.Build(entryPoint, requestedExports, roots);
}
