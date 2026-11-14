using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed class UnsafeAccessorTargetResolver : IUnsafeAccessorTargetResolver
{
    private readonly IUnsafeAccessorSignatureReader _signatures;
    private readonly ITypeDefinitionResolver _definitions;
    private readonly ImmutableDictionary<UnsafeAccessorMemberKind, IUnsafeAccessorMemberResolver> _members;
    private readonly ITypeFinder _types;
    private readonly IMethodRepository _methods;
    private readonly IMetadataStackTypeResolver _stackTypes;
    private readonly IUnsafeAccessorTypeNameResolver _typeNames;

    public UnsafeAccessorTargetResolver(
        IUnsafeAccessorSignatureReader signatures,
        ITypeDefinitionResolver definitions,
        ImmutableDictionary<UnsafeAccessorMemberKind, IUnsafeAccessorMemberResolver> members,
        ITypeFinder types,
        IMethodRepository methods,
        IMetadataStackTypeResolver stackTypes,
        IUnsafeAccessorTypeNameResolver typeNames)
    {
        _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _members = members ?? throw new ArgumentNullException(nameof(members));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        _stackTypes = stackTypes ?? throw new ArgumentNullException(nameof(stackTypes));
        _typeNames = typeNames ?? throw new ArgumentNullException(nameof(typeNames));
        if (members.Count != Enum.GetValues<UnsafeAccessorMemberKind>().Length ||
            Enum.GetValues<UnsafeAccessorMemberKind>().Any(kind => !members.ContainsKey(kind)))
            throw new ArgumentException("Every accessor kind requires exactly one member resolver.", nameof(members));
    }

    public UnsafeAccessorBinding Resolve(MethodInstanceModel accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        var declaration = accessor.Definition.UnsafeAccessor ??
            throw new ArgumentException("The method is not an unsafe accessor declaration.", nameof(accessor));
        if (declaration.IsMalformed || !accessor.Definition.IsStatic ||
            !_members.TryGetValue((UnsafeAccessorMemberKind)declaration.Kind, out var members))
            return Failure(UnsafeAccessorFailure.BadImageFormat);

        var kind = (UnsafeAccessorMemberKind)declaration.Kind;
        var signature = _signatures.Read(accessor.Definition);
        if (declaration.TypeTranslations is { } translations)
        {
            // A name the program does not define fails at the call, as the CLR fails to load it.
            if (Translate(signature, translations, accessor.Definition.Key.Assembly) is not { } translated)
                return Failure(UnsafeAccessorFailure.TypeLoad);
            signature = translated;
        }
        var resultType = Unmodified(signature.ReturnType);
        var isConstructor = kind == UnsafeAccessorMemberKind.Constructor;
        var isField = kind is UnsafeAccessorMemberKind.Field or UnsafeAccessorMemberKind.StaticField;
        if (isConstructor
            ? resultType.StackKind == CliValueKind.Void || resultType.Shape == CliTypeShape.ManagedByReference ||
                !string.IsNullOrEmpty(declaration.Name)
            : signature.ParameterTypes.IsEmpty)
            return Failure(UnsafeAccessorFailure.BadImageFormat);

        if (isField && (signature.ParameterTypes.Length != 1 || resultType.Shape != CliTypeShape.ManagedByReference))
            return Failure(UnsafeAccessorFailure.BadImageFormat);

        var receiver = isConstructor ? resultType : Unmodified(signature.ParameterTypes[0]);
        var owner = receiver.Shape == CliTypeShape.ManagedByReference ? Unmodified(receiver.ElementType!) : receiver;
        if (owner.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter or
            CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer ||
            owner.StackKind == CliValueKind.Void)
            return Failure(UnsafeAccessorFailure.BadImageFormat);

        if (owner.Shape is CliTypeShape.Array or CliTypeShape.SzArray)
            throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata,
                "UnsafeAccessor members on array owners are not supported"));

        var ownerDefinition = _definitions.ResolveTypeIdentity(owner);
        if (kind is UnsafeAccessorMemberKind.Method or UnsafeAccessorMemberKind.Field &&
            ownerDefinition.IsValueType && receiver.Shape != CliTypeShape.ManagedByReference)
            return Failure(UnsafeAccessorFailure.BadImageFormat);

        var name = isConstructor ? ".ctor" : declaration.NameSpecified ? declaration.Name ?? "" : accessor.Definition.Name;
        var match = members.Resolve(new(kind, name, accessor.Definition, signature, ownerDefinition));
        if (match is UnsafeAccessorMemberMatch.Failure failure)
            return Failure(failure.Kind);

        var closedOwner = _stackTypes.Resolve(owner, new(accessor.DeclaringType.TypeArguments, accessor.MethodArguments));
        var context = new CliGenericContext(closedOwner.TypeArguments, accessor.MethodArguments);
        return match switch
        {
            UnsafeAccessorMemberMatch.Method method => BindMethod(method.Definition),
            UnsafeAccessorMemberMatch.Field field => new UnsafeAccessorBinding.Field(new(
                field.Definition, closedOwner, _stackTypes.Resolve(field.Definition.SignatureType, context))),
            _ => throw new InvalidOperationException("The member resolver returned an invalid result."),
        };

        UnsafeAccessorBinding BindMethod(MethodDefinitionModel method)
        {
            var instance = new MethodInstanceModel(method, closedOwner, accessor.MethodArguments,
                _stackTypes.Resolve(method.Signature, context));
            return isConstructor ? new UnsafeAccessorBinding.Constructor(instance) : new UnsafeAccessorBinding.Method(instance);
        }
    }

    // Each translated position stands for the type its UnsafeAccessorTypeAttribute names, so member matching
    // compares the target's signature with that type.
    private MethodSignature<CliTypeIdentity>? Translate(MethodSignature<CliTypeIdentity> signature,
        UnsafeAccessorTypeTranslations translations, AssemblyIdentity accessorAssembly)
    {
        var returnType = signature.ReturnType;
        if (translations.Return is { } returnName)
        {
            if (Translated(returnName, returnType) is not { } translatedReturn) return null;
            returnType = translatedReturn;
        }

        var parameters = signature.ParameterTypes.ToBuilder();
        for (var index = 0; index < parameters.Count; index++)
        {
            if (translations.Parameter(index) is not { } name) continue;
            if (Translated(name, parameters[index]) is not { } type) return null;
            parameters[index] = type;
        }
        return new MethodSignature<CliTypeIdentity>(signature.Header, returnType,
            signature.RequiredParameterCount, signature.GenericParameterCount, parameters.ToImmutable());

        CliTypeIdentity? Translated(string name, CliTypeIdentity declared)
        {
            if (!Unmodified(declared).Equals(ObjectType))
                throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata,
                    "UnsafeAccessorTypeAttribute is supported on object parameters and return values only"));
            if (_typeNames.Resolve(name, accessorAssembly) is not { } definition) return null;
            if (definition.IsValueType || definition.GenericArity != 0)
                throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata,
                    $"UnsafeAccessorTypeAttribute type '{name}' is a value type or generic, which is not supported"));
            return CliTypeIdentity.FromDefinition(definition);
        }
    }

    private static readonly CliTypeIdentity ObjectType =
        CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, isValueType: false);

    private UnsafeAccessorBinding.Failure Failure(UnsafeAccessorFailure failure)
    {
        var exception = _types.FindType(failure == UnsafeAccessorFailure.AmbiguousMatch
            ? "System.Reflection.AmbiguousMatchException"
            : $"System.{failure}Exception");
        var constructor = exception.Methods.Select(_methods.GetMethod)
            .Single(method => method.Name == ".ctor" && !method.IsStatic && method.Signature.ParameterTypes.IsEmpty);
        return new(new(constructor, CliTypeIdentity.FromDefinition(exception), [], constructor.Signature));
    }

    private static CliTypeIdentity Unmodified(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified)
            type = type.ElementType!;
        return type;
    }
}
