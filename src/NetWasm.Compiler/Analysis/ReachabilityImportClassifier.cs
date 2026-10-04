using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityImportClassifier(
    ITypeFinder typeFinder,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IDelegateTypeRecognizer delegateTypes,
    IJavaScriptAsyncBindingResolver javaScriptAsyncBindings,
    IRuntimeIntrinsicRegistry intrinsics,
    ISymbolFormatter symbols,
    IEnumMetadataRequirementClassifier enumMetadataRequirements,
    INativeDeclarationValidator nativeDeclarations) : IReachabilityImportClassifier
{
    private readonly IEnumMetadataRequirementClassifier _enumMetadataRequirements =
        enumMetadataRequirements ?? throw new ArgumentNullException(nameof(enumMetadataRequirements));
    private readonly INativeDeclarationValidator _nativeDeclarations =
        nativeDeclarations ?? throw new ArgumentNullException(nameof(nativeDeclarations));

    public ReachabilityImportAnalysis Classify(ReachabilityImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var method = request.Method.Definition;
        if (method.NativeImport is not null)
        {
            _nativeDeclarations.Validate(request.Method);
            var layoutTypes = request.Method.Signature.ParameterSignatureTypes
                .Prepend(request.Method.Signature.ReturnSignatureType)
                .Where(type => type.IsValueType &&
                    type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation)
                .Distinct()
                .ToImmutableArray();
            return new(true,
                [.. layoutTypes.Where(type => type.Shape == CliTypeShape.GenericInstantiation)],
                [],
                [.. layoutTypes.Select(type => typeDefinitions.ResolveTypeIdentity(type).Key).Distinct()],
                [], [], [], null, null, [], null);
        }
        var constructedTypes = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var allocatedTypes = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var types = ImmutableArray.CreateBuilder<EntityKey>();
        var fields = ImmutableArray.CreateBuilder<FieldInstanceModel>();
        var enqueuedMethods = ImmutableArray.CreateBuilder<MethodInstanceModel>();
        var exceptions = ImmutableArray.CreateBuilder<ReachabilityExceptionRequirement>();
        var hostCallbacks = ImmutableArray.CreateBuilder<HostCallbackDeclaration>();
        var isAsyncExportBoundary = request.IsOutwardBoundary ||
                                    method.JSExport is not null;
        var asyncBinding = method.JSImport is not null || isAsyncExportBoundary
            ? javaScriptAsyncBindings.Resolve(method)
            : null;
        if (asyncBinding is not null)
        {
            types.Add(typeDefinitions.ResolveTypeIdentity(asyncBinding.TaskType).Key);
            constructedTypes.Add(asyncBinding.TaskType);
            allocatedTypes.Add(asyncBinding.TaskType);
            fields.Add(asyncBinding.StatusField);
            if (request.IsProcessEntryPoint || isAsyncExportBoundary)
            {
                enqueuedMethods.Add(asyncBinding.GetVoidResult ??
                    throw new InvalidOperationException("An asynchronous boundary requires task fault observation."));
            }
            if (asyncBinding.ResultField is not null)
            {
                fields.Add(asyncBinding.ResultField);
            }
            if (asyncBinding.ValueTaskTaskField is not null)
            {
                fields.Add(asyncBinding.ValueTaskTaskField);
            }
            if (isAsyncExportBoundary && asyncBinding.ValueTaskAsTask is not null)
            {
                enqueuedMethods.Add(asyncBinding.ValueTaskAsTask);
            }
            if (isAsyncExportBoundary)
            {
                exceptions.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
            }
        }
        if (request.Method.DeclaringType.Shape == CliTypeShape.GenericInstantiation)
        {
            constructedTypes.Add(request.Method.DeclaringType);
        }
        if (intrinsics.TryGetIntrinsic(method.Key, out var intrinsic))
        {
            var enumRequirements = ImmutableArray.CreateBuilder<EnumMetadataRequirement>();
            var payload = EnumMetadataPayload.None;
            if (IsEnumIntrinsic(intrinsic))
            {
                var enumTypes = ImmutableArray.CreateBuilder<EntityKey>();
                AddClosedEnumTypes(request.Method.MethodArguments, enumTypes);
                types.AddRange(enumTypes);
                payload = _enumMetadataRequirements.Classify(
                    intrinsic, !request.Method.MethodArguments.IsEmpty, method.Name);
                if (payload != EnumMetadataPayload.None)
                {
                    if (request.Method.MethodArguments.IsEmpty)
                    {
                        enumRequirements.Add(new(null, payload));
                    }
                    else
                    {
                        foreach (var enumType in enumTypes)
                        {
                            enumRequirements.Add(new(enumType, payload));
                        }
                    }
                }
                if ((payload & EnumMetadataPayload.Names) != 0)
                {
                    var stringDefinition = typeFinder.FindType("System.String");
                    var stringType = CliTypeIdentity.Named(
                        stringDefinition.Key.Assembly,
                        stringDefinition.Namespace,
                        stringDefinition.Name,
                        isValueType: false);
                    types.Add(stringDefinition.Key);
                    allocatedTypes.Add(stringType);
                }
            }
            if (intrinsic is RuntimeIntrinsic.EnumCompareTo or
                RuntimeIntrinsic.EnumToObject)
            {
                exceptions.Add(new(ManagedExceptionKind.Argument, "System.ArgumentException"));
            }
            if (intrinsic == RuntimeIntrinsic.EnumToObject)
            {
                exceptions.Add(new(
                    ManagedExceptionKind.ArgumentNull,
                    "System.ArgumentNullException"));
            }
            if (intrinsic == RuntimeIntrinsic.EnumGetValues && request.Method.MethodArguments.IsEmpty)
            {
                exceptions.Add(new(ManagedExceptionKind.NotSupported, "System.NotSupportedException"));
            }
            if (intrinsic == RuntimeIntrinsic.EnumConvert)
            {
                exceptions.Add(new(
                    ManagedExceptionKind.ArgumentNull,
                    "System.ArgumentNullException"));
                exceptions.Add(new(
                    ManagedExceptionKind.InvalidCast,
                    "System.InvalidCastException"));
            }
            if (intrinsic is RuntimeIntrinsic.EnumToString or RuntimeIntrinsic.EnumFormat ||
                intrinsic == RuntimeIntrinsic.EnumConvert &&
                method.Name is "InternalToType" or "System.IConvertible.ToType")
            {
                enqueuedMethods.Add(ResolveEnumFormat());
            }
            if (intrinsic == RuntimeIntrinsic.ValueTypeEquals)
            {
                enqueuedMethods.Add(ResolveObjectEquals());
            }
            if (intrinsic == RuntimeIntrinsic.ValueTypeGetHashCode)
            {
                enqueuedMethods.Add(ResolveValueTypeHashCode());
            }
            return new(
                true,
                constructedTypes.ToImmutable(),
                allocatedTypes.ToImmutable(),
                types.ToImmutable(),
                fields.ToImmutable(),
                enqueuedMethods.ToImmutable(),
                exceptions.ToImmutable(),
                null,
                null,
                hostCallbacks.ToImmutable(),
                asyncBinding)
            {
                EnumMetadataRequirements = enumRequirements.ToImmutable(),
            };
        }
        if (method.JSImport is not null)
        {
            if (asyncBinding is not null)
            {
                enqueuedMethods.Add(asyncBinding.SetResult);
                enqueuedMethods.Add(asyncBinding.SetException);
                enqueuedMethods.Add(asyncBinding.SetCanceled);
                exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
            }
            AddHostCallbacks(method, types, allocatedTypes, constructedTypes, exceptions, hostCallbacks);
            exceptions.Add(new(ManagedExceptionKind.JSException, "System.JSException"));
            if (method.Signature.ReturnSignatureType.CanonicalName == "primitive:string")
            {
                exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
            }
            AddByteArrayReturn(method, types, allocatedTypes, constructedTypes, exceptions);
            AddHostObjectReturn(method, types, allocatedTypes, exceptions);
            return new(
                true,
                constructedTypes.ToImmutable(),
                allocatedTypes.ToImmutable(),
                types.ToImmutable(),
                fields.ToImmutable(),
                enqueuedMethods.ToImmutable(),
                exceptions.ToImmutable(),
                method,
                null,
                hostCallbacks.ToImmutable(),
                asyncBinding);
        }
        if (method.WitImport is not null)
        {
            return new(
                true,
                constructedTypes.ToImmutable(),
                allocatedTypes.ToImmutable(),
                types.ToImmutable(),
                fields.ToImmutable(),
                enqueuedMethods.ToImmutable(),
                exceptions.ToImmutable(),
                null,
                method,
                hostCallbacks.ToImmutable(),
                asyncBinding);
        }
        if (!method.HasManagedBody)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                "reachable method has no CIL body and is not a declared runtime intrinsic",
                symbols.Format(method)));
        }
        return new(
            false,
            constructedTypes.ToImmutable(),
            allocatedTypes.ToImmutable(),
            types.ToImmutable(),
            fields.ToImmutable(),
            enqueuedMethods.ToImmutable(),
            exceptions.ToImmutable(),
            null,
            null,
            hostCallbacks.ToImmutable(),
            asyncBinding);
    }

    private MethodInstanceModel ResolveObjectEquals()
    {
        var objectType = typeFinder.FindType("System.Object");
        var method = objectType.Methods
            .Select(methods.GetMethod)
            .Single(candidate =>
                candidate.Name == "Equals" &&
                candidate.IsStatic &&
                candidate.Signature.ParameterTypes.Length == 2);
        var declaringType = CliTypeIdentity.Named(
            objectType.Key.Assembly,
            objectType.Namespace,
            objectType.Name,
            isValueType: false);
        return new(method, declaringType, [], method.Signature);
    }

    private MethodInstanceModel ResolveValueTypeHashCode()
    {
        var objectType = typeFinder.FindType("System.Object");
        var method = objectType.Methods
            .Select(methods.GetMethod)
            .Single(candidate =>
                candidate.Name == "GetValueHashCode" &&
                candidate.IsStatic &&
                candidate.Signature.ParameterTypes.Length == 1);
        var declaringType = CliTypeIdentity.Named(
            objectType.Key.Assembly,
            objectType.Namespace,
            objectType.Name,
            isValueType: false);
        return new(method, declaringType, [], method.Signature);
    }

    private MethodInstanceModel ResolveEnumFormat()
    {
        var algorithmsType = typeFinder.FindType("System.EnumAlgorithms");
        var method = algorithmsType.Methods
            .Select(methods.GetMethod)
            .Single(candidate =>
                candidate.Name == "Format" &&
                candidate.IsStatic &&
                candidate.Signature.ParameterSignatureTypes.Length == 3 &&
                candidate.Signature.ParameterSignatureTypes[0].StackKind ==
                    CliValueKind.NativeInt);
        var declaringType = CliTypeIdentity.Named(
            algorithmsType.Key.Assembly,
            algorithmsType.Namespace,
            algorithmsType.Name,
            isValueType: false);
        return new(method, declaringType, [], method.Signature);
    }

    private static bool IsEnumIntrinsic(RuntimeIntrinsic intrinsic) => intrinsic is
        RuntimeIntrinsic.EnumEquals or
        RuntimeIntrinsic.EnumGetHashCode or
        RuntimeIntrinsic.EnumCompareTo or
        RuntimeIntrinsic.EnumGetTypeCode or
        RuntimeIntrinsic.EnumHasFlag or
        RuntimeIntrinsic.EnumGetNames or
        RuntimeIntrinsic.EnumGetName or
        RuntimeIntrinsic.EnumGetValues or
        RuntimeIntrinsic.EnumIsDefined or
        RuntimeIntrinsic.EnumGetMetadata or
        RuntimeIntrinsic.EnumGetUnderlyingType or
        RuntimeIntrinsic.EnumToString or
        RuntimeIntrinsic.EnumFormat or
        RuntimeIntrinsic.EnumToObject or
        RuntimeIntrinsic.EnumConvert;

    private void AddClosedEnumTypes(
        ImmutableArray<CliTypeIdentity> methodArguments,
        ImmutableArray<EntityKey>.Builder types)
    {
        foreach (var argument in methodArguments)
        {
            if (argument.ContainsGenericParameters ||
                argument.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
            {
                continue;
            }
            var definition = typeDefinitions.ResolveTypeIdentity(argument);
            if (definition.IsEnum)
            {
                types.Add(definition.Key);
            }
        }
    }

    private void AddHostCallbacks(
        MethodDefinitionModel method,
        ImmutableArray<EntityKey>.Builder types,
        ImmutableArray<CliTypeIdentity>.Builder allocatedTypes,
        ImmutableArray<CliTypeIdentity>.Builder constructedTypes,
        ImmutableArray<ReachabilityExceptionRequirement>.Builder exceptions,
        ImmutableArray<HostCallbackDeclaration>.Builder callbacks)
    {
        for (var parameterIndex = 0;
             parameterIndex < method.Signature.ParameterSignatureTypes.Length;
             parameterIndex++)
        {
            var parameter = method.Signature.ParameterSignatureTypes[parameterIndex];
            if (!delegateTypes.Recognize(parameter))
            {
                continue;
            }
            var delegateType = typeDefinitions.ResolveTypeIdentity(parameter);
            var invoke = delegateType.Methods
                .Select(methods.GetMethod)
                .Single(candidate => candidate.Name == "Invoke");
            var invokeSignature = invoke.Signature.Substitute(
                parameter.Shape == CliTypeShape.GenericInstantiation
                    ? parameter.TypeArguments
                    : []);
            types.Add(delegateType.Key);
            callbacks.Add(new(
                method.Key,
                parameterIndex,
                new MethodInstanceModel(invoke, parameter, [], invokeSignature),
                $"netwasm.callback.{method.Key.MetadataToken:x8}.{parameterIndex}"));
            foreach (var callbackParameter in invokeSignature.ParameterSignatureTypes)
            {
                if (callbackParameter.CanonicalName == "primitive:string")
                {
                    types.Add(typeFinder.FindType("System.String").Key);
                    exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                }
                if (IsByteArray(callbackParameter))
                {
                    allocatedTypes.Add(callbackParameter);
                    constructedTypes.Add(callbackParameter);
                    types.Add(typeFinder.FindType("System.Array").Key);
                    types.Add(typeFinder.FindType("System.Byte").Key);
                    exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                }
            }
        }
    }

    private void AddByteArrayReturn(
        MethodDefinitionModel method,
        ImmutableArray<EntityKey>.Builder types,
        ImmutableArray<CliTypeIdentity>.Builder allocatedTypes,
        ImmutableArray<CliTypeIdentity>.Builder constructedTypes,
        ImmutableArray<ReachabilityExceptionRequirement>.Builder exceptions)
    {
        if (!IsByteArray(method.Signature.ReturnSignatureType))
        {
            return;
        }
        allocatedTypes.Add(method.Signature.ReturnSignatureType);
        constructedTypes.Add(method.Signature.ReturnSignatureType);
        types.Add(typeFinder.FindType("System.Array").Key);
        types.Add(typeFinder.FindType("System.Byte").Key);
        exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
    }

    private void AddHostObjectReturn(
        MethodDefinitionModel method,
        ImmutableArray<EntityKey>.Builder types,
        ImmutableArray<CliTypeIdentity>.Builder allocatedTypes,
        ImmutableArray<ReachabilityExceptionRequirement>.Builder exceptions)
    {
        var returnType = method.Signature.ReturnSignatureType;
        if (!IsHostObject(returnType))
        {
            return;
        }
        types.Add(typeDefinitions.ResolveTypeIdentity(returnType).Key);
        // The import adapter allocates these wrappers without a CIL newobj.
        // Register the runtime producer too, so interface dispatch can discover
        // their implementations. Parameter-only references allocate nothing.
        allocatedTypes.Add(returnType);
        exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
    }

    private static bool IsByteArray(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray && type.ElementType!.CanonicalName == "primitive:u1";

    private static bool IsHostObject(CliTypeIdentity type) =>
        type.FullName is
            "System.Runtime.InteropServices.JavaScript.JSObject" or
            "System.Runtime.InteropServices.JavaScript.JSSubscription";
}
