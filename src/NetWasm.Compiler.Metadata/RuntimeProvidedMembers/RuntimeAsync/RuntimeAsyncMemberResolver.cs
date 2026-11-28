// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// The CoreLib members the runtime async lowering calls (System.Runtime.CompilerServices.RuntimeAsync and the delegates
// it takes), resolved once per compilation and only when a runtime-async method is lowered.
internal sealed class RuntimeAsyncMemberResolver : IRuntimeAsyncMemberResolver
{
    private const string RuntimeAsyncName = "System.Runtime.CompilerServices.RuntimeAsync";
    private const string AsyncHelpersName = "System.Runtime.CompilerServices.AsyncHelpers";

    private readonly IMethodRepository _methods;
    private readonly IFieldRepository _fields;
    private readonly ITypeDefinitionResolver _definitions;
    private readonly IMetadataStackTypeResolver _stackTypes;
    private readonly Lazy<ITypeFinder> _types;
    private readonly Lazy<TypeDefinitionModel> _runtime;
    private readonly Lazy<EntityKey> _asyncHelpers;
    private readonly Lazy<TypeDefinitionModel> _exception;
    private readonly Lazy<FieldInstanceModel> _contextsChanged;

    public RuntimeAsyncMemberResolver(
        ITypeFinderFactory types,
        MetadataCompilationSnapshot snapshot,
        IMethodRepository methods,
        IFieldRepository fields,
        ITypeDefinitionResolver definitions,
        IMetadataStackTypeResolver stackTypes)
    {
        _stackTypes = stackTypes;
        _methods = methods;
        _fields = fields;
        _definitions = definitions;
        _types = new(() => types.Create(snapshot));
        _runtime = new(() => _types.Value.FindType(RuntimeAsyncName));
        _asyncHelpers = new(() => _types.Value.FindType("System.Runtime.CompilerServices.AsyncHelpers").Key);
        _exception = new(() => _types.Value.FindType("System.Exception"));
        _contextsChanged = new(() =>
        {
            var owner = _types.Value.FindType("System.Threading.AmbientContextChanges");
            var field = owner.Fields.Select(_fields.GetField).Single(field => field.Name == "Made" && field.IsStatic);
            return new FieldInstanceModel(field, CliTypeIdentity.FromDefinition(owner), field.SignatureType);
        });
    }

    public EntityKey AsyncHelpers => _asyncHelpers.Value;

    public TypeDefinitionModel Exception => _exception.Value;

    public FieldInstanceModel ContextsChanged => _contextsChanged.Value;

    public MethodInstanceModel Resolve(RuntimeAsyncMember member) => member switch
    {
        RuntimeAsyncMember.Named named => Runtime(named.Name, named.MethodArguments.IsDefault ? [] : named.MethodArguments,
            _ => true),
        RuntimeAsyncMember.ForAwaited awaited => Runtime(awaited.Name,
            awaited.Awaited.Shape == CliTypeShape.GenericInstantiation ? awaited.Awaited.TypeArguments : [],
            candidate => DefinitionName(candidate.Signature.ParameterSignatureTypes[0]) == DefinitionName(awaited.Awaited)),
        RuntimeAsyncMember.Constructor constructor => Constructor(constructor.Constructed),
        RuntimeAsyncMember.ConfigureAwait configure => ConfigureAwait(configure.Awaitable),
        RuntimeAsyncMember.AsyncHelpersNamed named => Find(_types.Value.FindType(AsyncHelpersName), named.Name,
            named.MethodArguments.IsDefault ? [] : named.MethodArguments, _ => true),
        RuntimeAsyncMember.AsyncHelpersForAwaited awaited => Find(_types.Value.FindType(AsyncHelpersName), awaited.Name,
            awaited.Awaited.Shape == CliTypeShape.GenericInstantiation ? awaited.Awaited.TypeArguments : [],
            candidate => candidate.Signature.ParameterSignatureTypes.Length == 1 &&
                DefinitionName(candidate.Signature.ParameterSignatureTypes[0]) == DefinitionName(awaited.Awaited)),
        _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };

    private MethodInstanceModel Find(TypeDefinitionModel owner, string name, ImmutableArray<CliTypeIdentity> methodArguments,
        Func<MethodDefinitionModel, bool> filter)
    {
        var method = owner.Methods.Select(_methods.GetMethod)
            .Single(candidate => candidate.Name == name && candidate.GenericArity == methodArguments.Length &&
                filter(candidate));
        methodArguments = [.. methodArguments.Select(_stackTypes.Resolve)];
        return new(method, CliTypeIdentity.FromDefinition(owner), methodArguments,
            _stackTypes.Resolve(method.Signature.Substitute([], methodArguments)));
    }

    private MethodInstanceModel Runtime(string name, ImmutableArray<CliTypeIdentity> methodArguments,
        Func<MethodDefinitionModel, bool> filter)
    {
        var method = _runtime.Value.Methods.Select(_methods.GetMethod)
            .Single(candidate => candidate.Name == name && candidate.GenericArity == methodArguments.Length &&
                filter(candidate));
        methodArguments = [.. methodArguments.Select(_stackTypes.Resolve)];
        return new(method, CliTypeIdentity.FromDefinition(_runtime.Value), methodArguments,
            _stackTypes.Resolve(method.Signature.Substitute([], methodArguments)));
    }

    private MethodInstanceModel ConfigureAwait(CliTypeIdentity awaitable)
    {
        var definition = _definitions.ResolveTypeIdentity(awaitable);
        var method = definition.Methods.Select(_methods.GetMethod).Single(method => method.Name == "ConfigureAwait" &&
            !method.IsStatic && method.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.Primitive }]);
        return new(method, awaitable, [], _stackTypes.Resolve(method.Signature.Substitute(awaitable.TypeArguments)));
    }

    private MethodInstanceModel Constructor(CliTypeIdentity constructed)
    {
        var definition = _definitions.ResolveTypeIdentity(constructed);
        var constructor = definition.Methods.Select(_methods.GetMethod).Single(method => method.Name == ".ctor");
        return new(constructor, constructed, [], _stackTypes.Resolve(constructor.Signature.Substitute(constructed.TypeArguments)));
    }

    // The definition a helper's awaited parameter names, through a by-reference parameter (the virtual await's helpers
    // take the task by address).
    private static string? DefinitionName(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.ManagedByReference => DefinitionName(type.ElementType!),
        CliTypeShape.GenericInstantiation => type.ElementType!.FullName,
        _ => type.FullName,
    };
}
