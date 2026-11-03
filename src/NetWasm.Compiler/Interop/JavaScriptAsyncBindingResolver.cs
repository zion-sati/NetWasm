using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Interop;

internal interface IJavaScriptAsyncBindingResolver
{
    JavaScriptAsyncMethodBinding? Resolve(MethodDefinitionModel method);
}

internal sealed class JavaScriptAsyncBindingResolver(
    ITypeFinder types,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver identities,
    IFieldRepository fields,
    IMethodRepository methodRepository,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    PlatformWorldCapabilities world) : IJavaScriptAsyncBindingResolver
{
    private const string AsyncEntryPointType = "System.Runtime.InteropServices.AsyncEntryPoint";

    private readonly ITypeFinder _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolver _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methodRepository = methodRepository ??
        throw new ArgumentNullException(nameof(methodRepository));
    private readonly IMethodInstanceResolver _methodInstances = methodInstances ??
        throw new ArgumentNullException(nameof(methodInstances));
    private readonly ISymbolFormatter _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));
    private readonly PlatformWorldCapabilities _world = world ??
        throw new ArgumentNullException(nameof(world));

    public JavaScriptAsyncMethodBinding? Resolve(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var asyncReturn = JavaScriptAsyncSignature.Classify(
            method.Signature.ReturnSignatureType);
        if (!asyncReturn.IsAsync)
        {
            return null;
        }

        var taskBaseDefinition = _types.FindType("System.Threading.Tasks.Task");
        var taskDefinition = _types.FindType(asyncReturn.HasResult
            ? "System.Threading.Tasks.Task`1"
            : "System.Threading.Tasks.Task");
        var typeArguments = asyncReturn.ResultType is null
            ? ImmutableArray<CliTypeIdentity>.Empty
            : [asyncReturn.ResultType];
        var taskType = typeArguments.IsEmpty
            ? _identities.GetTypeIdentity(taskDefinition.Key)
            : CliTypeIdentity.GenericInstantiation(
                _identities.GetTypeIdentity(taskDefinition.Key),
                typeArguments);
        var resultMethods = taskDefinition.Methods
            .Select(_methodRepository.GetMethod)
            .ToArray();
        var setResult = resultMethods.Single(candidate =>
            candidate.Name == "SetResult" &&
            candidate.Signature.ParameterSignatureTypes.Length ==
                (asyncReturn.HasResult ? 1 : 0));
        var commonMethods = taskBaseDefinition.Methods
            .Select(_methodRepository.GetMethod)
            .ToArray();
        var setException = commonMethods.Single(candidate =>
            candidate.Name == "SetExceptionForJavaScriptInterop" &&
            candidate.Signature.ParameterSignatureTypes.IsEmpty);
        var setCanceled = commonMethods.Single(candidate =>
            candidate.Name == "SetCanceledForJavaScriptInterop" &&
            candidate.Signature.ParameterSignatureTypes.IsEmpty);
        var taskBaseType = _identities.GetTypeIdentity(taskBaseDefinition.Key);
        var statusField = ResolveField(
            taskBaseDefinition,
            taskBaseType,
            ImmutableArray<CliTypeIdentity>.Empty,
            "_status");
        var resultField = asyncReturn.HasResult
            ? ResolveField(taskDefinition, taskType, typeArguments, "_result")
            : null;
        var binding = new JavaScriptAsyncMethodBinding(
            method.Key,
            asyncReturn,
            taskType,
            Resolve(setResult, typeArguments),
            Resolve(setException, ImmutableArray<CliTypeIdentity>.Empty),
            Resolve(setCanceled, ImmutableArray<CliTypeIdentity>.Empty))
        {
            StatusField = statusField,
            ResultField = resultField,
            GetVoidResult = Resolve(commonMethods.Single(candidate =>
                candidate.Name == "GetVoidResult" &&
                candidate.Signature.ParameterSignatureTypes.IsEmpty), []),
        };
        if (asyncReturn.Kind != JavaScriptAsyncReturnKind.ValueTask)
        {
            return binding with { CommandEntryPoint = ResolveCommandEntryPoint(asyncReturn) };
        }

        var valueTaskDefinition = _typeDefinitions.ResolveTypeIdentity(
            method.Signature.ReturnSignatureType);
        var valueTaskArguments = asyncReturn.ResultType is null
            ? ImmutableArray<CliTypeIdentity>.Empty
            : [asyncReturn.ResultType];
        var asTask = valueTaskDefinition.Methods
            .Select(_methodRepository.GetMethod)
            .Single(candidate => candidate.Name == "AsTask" &&
                candidate.Signature.ParameterSignatureTypes.IsEmpty);
        return binding with
        {
            ValueTaskTaskField = ResolveField(
                valueTaskDefinition,
                method.Signature.ReturnSignatureType,
                valueTaskArguments,
                "_task"),
            ValueTaskAsTask = Resolve(asTask, valueTaskArguments),
        };
    }

    // An entry point returns Task or Task<int>; a world with a host-driven reactor keeps the
    // asynchronous process boundary instead.
    private MethodInstanceModel? ResolveCommandEntryPoint(JavaScriptAsyncReturn asyncReturn)
    {
        if (_world.ReactorHost ||
            asyncReturn.ResultType is { } result &&
            result.CanonicalName != CliTypeIdentity.FromStackKind(CliValueKind.I4).CanonicalName)
        {
            return null;
        }

        // The type holds only the Run overloads for Task and Task<int>.
        var run = _types.FindType(AsyncEntryPointType).Methods
            .Select(_methodRepository.GetMethod)
            .Single(candidate =>
                (candidate.Signature.ReturnType != CliValueKind.Void) == asyncReturn.HasResult);
        return Resolve(run, ImmutableArray<CliTypeIdentity>.Empty);
    }

    private MethodInstanceModel Resolve(
        MethodDefinitionModel method,
        ImmutableArray<CliTypeIdentity> typeArguments) =>
        _methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            _symbols.Format(method),
            0,
            new CliGenericContext(typeArguments, []));

    private FieldInstanceModel ResolveField(
        TypeDefinitionModel definition,
        CliTypeIdentity declaringType,
        ImmutableArray<CliTypeIdentity> typeArguments,
        string name)
    {
        var field = definition.Fields
            .Select(_fields.GetField)
            .Single(candidate => candidate.Name == name);
        return new(
            field,
            declaringType,
            field.SignatureType.Substitute(typeArguments));
    }
}
