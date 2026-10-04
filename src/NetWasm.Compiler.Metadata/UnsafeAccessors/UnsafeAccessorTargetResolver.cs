using System;
using System.Collections.Immutable;
using System.Linq;
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

    public UnsafeAccessorTargetResolver(
        IUnsafeAccessorSignatureReader signatures,
        ITypeDefinitionResolver definitions,
        ImmutableDictionary<UnsafeAccessorMemberKind, IUnsafeAccessorMemberResolver> members,
        ITypeFinder types,
        IMethodRepository methods,
        IMetadataStackTypeResolver stackTypes)
    {
        _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _members = members ?? throw new ArgumentNullException(nameof(members));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        _stackTypes = stackTypes ?? throw new ArgumentNullException(nameof(stackTypes));
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

        if (declaration.HasTypeTranslation)
            throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata,
                "UnsafeAccessorTypeAttribute type translation is not supported"));

        var kind = (UnsafeAccessorMemberKind)declaration.Kind;
        var signature = _signatures.Read(accessor.Definition);
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
