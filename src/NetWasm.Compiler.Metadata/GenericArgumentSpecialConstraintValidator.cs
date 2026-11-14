using System;
using System.Linq;
using System.Reflection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Metadata;

public sealed class GenericArgumentSpecialConstraintValidator(
    ITypeDefinitionResolver definitions,
    IMethodRepository methods,
    INullableTypeResolver nullableTypes) : IGenericArgumentSpecialConstraintValidator
{
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ?? throw new ArgumentNullException(nameof(nullableTypes));

    public void Validate(GenericArgumentSpecialConstraintRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var flags = request.Attributes & GenericParameterAttributes.SpecialConstraintMask;
        var argument = request.Argument;
        // A definition may be decoded before its caller is instantiated. Validate
        // again when MethodSpec substitution supplies the actual argument.
        if (argument.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter)
            return;
        var array = argument.Shape is CliTypeShape.Array or CliTypeShape.SzArray;
        var definition = array ? null : _definitions.ResolveTypeIdentity(argument);
        const GenericParameterAttributes allowsByRefLike = (GenericParameterAttributes)0x20;
        if (definition?.IsByRefLike == true && (request.Attributes & allowsByRefLike) == 0)
            throw Failure(request, "requires a generic parameter that allows ref struct");
        var valueType = definition?.IsValueType == true;
        if ((flags & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && valueType)
            throw Failure(request, "requires a reference type");
        if ((flags & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 &&
            (!valueType || _nullableTypes.Resolve(argument) is not null))
            throw Failure(request, "requires a non-nullable value type");
        if ((flags & GenericParameterAttributes.DefaultConstructorConstraint) != 0 && !valueType &&
            (definition is null || definition.IsAbstract || !definition.Methods.Select(_methods.GetMethod).Any(method =>
                method.Name == ".ctor" && !method.IsStatic && method.IsPublic && method.Signature.ParameterTypes.IsEmpty)))
            throw Failure(request, "requires a public parameterless constructor on a non-abstract type");
    }

    private static CompilerException Failure(GenericArgumentSpecialConstraintRequest request, string reason) =>
        new(new CompilerDiagnostic(DiagnosticCode.AssemblyResolution,
            $"Generic argument '{request.Argument}' {reason}.", request.Caller, request.IlOffset));
}
