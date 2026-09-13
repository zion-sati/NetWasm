using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
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
    ISymbolFormatter symbols) : IReachabilityImportClassifier
{
    public ReachabilityImportAnalysis Classify(ReachabilityImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var method = request.Method.Definition;
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
            if (IsEnumIntrinsic(intrinsic))
            {
                AddClosedEnumTypes(request.Method.MethodArguments, types);
                var stringDefinition = typeFinder.FindType("System.String");
                var stringType = CliTypeIdentity.Named(
                    stringDefinition.Key.Assembly,
                    stringDefinition.Namespace,
                    stringDefinition.Name,
                    isValueType: false);
                types.Add(stringDefinition.Key);
                allocatedTypes.Add(stringType);
            }
            if (symbols.Format(method.DeclaringType) == "System.Enum" &&
                method.Name == "InternalGetValuesAsUnderlyingType" &&
                request.Method.MethodArguments.Length == 1)
            {
                var enumDefinition = typeDefinitions.ResolveTypeIdentity(
                    request.Method.MethodArguments[0]);
                constructedTypes.Add(CliTypeIdentity.SzArray(
                    enumDefinition.EnumUnderlyingType));
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
            if (intrinsic == RuntimeIntrinsic.EnumConvert)
            {
                exceptions.Add(new(
                    ManagedExceptionKind.ArgumentNull,
                    "System.ArgumentNullException"));
                exceptions.Add(new(
                    ManagedExceptionKind.InvalidCast,
                    "System.InvalidCastException"));
            }
            if (intrinsic == RuntimeIntrinsic.EnumParse)
            {
                exceptions.Add(new(
                    ManagedExceptionKind.ArgumentNull,
                    "System.ArgumentNullException"));
                exceptions.Add(new(ManagedExceptionKind.Argument, "System.ArgumentException"));
                exceptions.Add(new(ManagedExceptionKind.Overflow, "System.OverflowException"));
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
                asyncBinding);
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
            AddHostObjectReturn(method, types, exceptions);
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
        if (!method.HasBody)
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
        RuntimeIntrinsic.EnumParse or
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
            if (argument.Shape != CliTypeShape.Named)
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
        ImmutableArray<ReachabilityExceptionRequirement>.Builder exceptions)
    {
        if (!IsHostObject(method.Signature.ReturnSignatureType) &&
            !method.Signature.ParameterSignatureTypes.Any(IsHostObject))
        {
            return;
        }
        if (IsHostObject(method.Signature.ReturnSignatureType))
        {
            types.Add(typeDefinitions.ResolveTypeIdentity(method.Signature.ReturnSignatureType).Key);
            exceptions.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
        }
    }

    private static bool IsByteArray(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray && type.ElementType!.CanonicalName == "primitive:u1";

    private static bool IsHostObject(CliTypeIdentity type) =>
        type.FullName is
            "System.Runtime.InteropServices.JavaScript.JSObject" or
            "System.Runtime.InteropServices.JavaScript.JSSubscription";
}
