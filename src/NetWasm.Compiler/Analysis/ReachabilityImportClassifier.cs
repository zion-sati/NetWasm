using System;
using System.Collections.Generic;
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
    INativeDeclarationValidator nativeDeclarations,
    IJavaScriptConversionResolver conversions) : IReachabilityImportClassifier
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
            // The host supplies a [WasmImportLinkage] function; its throw raises JSException.
            ImmutableArray<ReachabilityExceptionRequirement> nativeExceptions = method.NativeImport.HasWasmImportLinkage
                ? [new(ManagedExceptionKind.JSException, "System.JSException")]
                : [];
            return new(true,
                [.. layoutTypes.Where(type => type.Shape == CliTypeShape.GenericInstantiation)],
                [],
                [.. layoutTypes.Select(type => typeDefinitions.ResolveTypeIdentity(type).Key).Distinct()],
                [], [], nativeExceptions, null, null, [], null);
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
            if (request.IsProcessEntryPoint && asyncBinding.CommandEntryPoint is { } commandEntryPoint)
            {
                enqueuedMethods.Add(commandEntryPoint);
            }
            else if (request.IsProcessEntryPoint || isAsyncExportBoundary)
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
            if (intrinsic is RuntimeIntrinsic.EnumToString or RuntimeIntrinsic.EnumFormat)
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
            // An async import's settled value is marshalled exactly like a synchronous result.
            var resultType = asyncBinding?.Return.ResultType ?? method.Signature.ReturnSignatureType;
            if (resultType.CanonicalName == "primitive:string")
            {
                exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
            }
            AddByteArrayReturn(resultType, types, allocatedTypes, constructedTypes, exceptions);
            AddHostObjectReturn(resultType, types, allocatedTypes, exceptions);
            var importConversions = AddConversions(
                ImportBoundary(method, asyncBinding, hostCallbacks), enqueuedMethods);
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
                asyncBinding)
            {
                Conversions = importConversions,
            };
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
        var exportConversions = method.JSExport is not null
            ? AddConversions(ExportBoundary(method, asyncBinding), enqueuedMethods)
            : ImmutableDictionary<string, MethodInstanceModel>.Empty;
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
            asyncBinding)
        {
            Conversions = exportConversions,
        };
    }

    // The values an import passes, each with the conversion it needs: its arguments
    // leave managed code; its result, settled value and callback arguments enter it,
    // and a callback's result leaves it.
    private static IEnumerable<string?> ImportBoundary(
        MethodDefinitionModel method,
        JavaScriptAsyncMethodBinding? asyncBinding,
        IEnumerable<HostCallbackDeclaration> callbacks) =>
        method.Signature.ParameterSignatureTypes.Select(From)
            .Append(To(method.Signature.ReturnSignatureType))
            .Append(asyncBinding?.Return.ResultType is { } result ? To(result) : null)
            .Concat(callbacks.SelectMany(callback => callback.Invoke.Signature.ParameterSignatureTypes.Select(To)
                .Append(From(callback.Invoke.Signature.ReturnSignatureType))));

    // An export's arguments enter managed code and its result leaves it.
    private static IEnumerable<string?> ExportBoundary(
        MethodDefinitionModel method,
        JavaScriptAsyncMethodBinding? asyncBinding) =>
        method.Signature.ParameterSignatureTypes.Select(To)
            .Append(From(asyncBinding?.Return.ResultType ?? method.Signature.ReturnSignatureType));

    private static string? To(CliTypeIdentity type) => JavaScriptConversion.ToManaged(JavaScriptNullable.Unwrap(type));

    private static string? From(CliTypeIdentity type) => JavaScriptConversion.FromManaged(JavaScriptNullable.Unwrap(type));

    // CoreLib's conversions stay out of a program whose boundaries pass nothing to convert.
    private ImmutableDictionary<string, MethodInstanceModel> AddConversions(
        IEnumerable<string?> names,
        ImmutableArray<MethodInstanceModel>.Builder enqueuedMethods)
    {
        var resolved = ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
        foreach (var name in names.OfType<string>().Distinct(StringComparer.Ordinal))
        {
            var conversion = conversions.Resolve(name);
            resolved.Add(name, conversion);
            enqueuedMethods.Add(conversion);
        }
        return resolved.ToImmutable();
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
                // The callback wrapper wraps a JSObject argument without a CIL newobj.
                AddHostObjectReturn(callbackParameter, types, allocatedTypes, exceptions);
                if (JavaScriptValueArray.IsSupported(callbackParameter))
                {
                    allocatedTypes.Add(callbackParameter);
                    constructedTypes.Add(callbackParameter);
                    types.Add(typeFinder.FindType("System.Array").Key);
                    types.Add(typeFinder.FindType(JavaScriptValueArray.GetElementTypeName(callbackParameter)).Key);
                    exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                }
            }
        }
    }

    private void AddByteArrayReturn(
        CliTypeIdentity returnType,
        ImmutableArray<EntityKey>.Builder types,
        ImmutableArray<CliTypeIdentity>.Builder allocatedTypes,
        ImmutableArray<CliTypeIdentity>.Builder constructedTypes,
        ImmutableArray<ReachabilityExceptionRequirement>.Builder exceptions)
    {
        if (!JavaScriptValueArray.IsSupported(returnType))
        {
            return;
        }
        allocatedTypes.Add(returnType);
        constructedTypes.Add(returnType);
        types.Add(typeFinder.FindType("System.Array").Key);
        types.Add(typeFinder.FindType(JavaScriptValueArray.GetElementTypeName(returnType)).Key);
        exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
    }

    private void AddHostObjectReturn(
        CliTypeIdentity returnType,
        ImmutableArray<EntityKey>.Builder types,
        ImmutableArray<CliTypeIdentity>.Builder allocatedTypes,
        ImmutableArray<ReachabilityExceptionRequirement>.Builder exceptions)
    {
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

    private static bool IsHostObject(CliTypeIdentity type) =>
        type.FullName is
            "System.Runtime.InteropServices.JavaScript.JSObject" or
            "System.Runtime.InteropServices.JavaScript.JSSubscription";
}
