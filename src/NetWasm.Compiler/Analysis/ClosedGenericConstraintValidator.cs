using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.GenericConstraints;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ClosedGenericConstraintValidator(
    IGenericParameterConstraintReader parameters,
    IGenericArgumentSpecialConstraintValidator specialConstraints,
    ITypeRelationshipClassifier relationships,
    ITypeDefinitionResolver definitions) : IClosedGenericConstraintValidator
{
    private readonly IGenericParameterConstraintReader _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    private readonly IGenericArgumentSpecialConstraintValidator _specialConstraints = specialConstraints ?? throw new ArgumentNullException(nameof(specialConstraints));
    private readonly ITypeRelationshipClassifier _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ConcurrentDictionary<CliTypeIdentity, bool> _validatedTypes = [];

    public void Validate(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.DeclaringType.ContainsGenericParameters || method.MethodArguments.Any(argument => argument.ContainsGenericParameters))
            return;
        Validate(method.DeclaringType);
        if (method.Definition.GenericArity == 0)
            return;
        var typeArguments = method.DeclaringType.Shape == CliTypeShape.GenericInstantiation ? method.DeclaringType.TypeArguments : [];
        ValidateGroup(method.Definition.Key, method.MethodArguments, typeArguments, method.MethodArguments, method.CanonicalName);
    }

    public void Validate(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Shape != CliTypeShape.GenericInstantiation || type.ContainsGenericParameters)
            return;
        _validatedTypes.GetOrAdd(type, ValidateType);
    }

    private bool ValidateType(CliTypeIdentity type)
    {
        var definition = _definitions.ResolveTypeIdentity(type);
        ValidateGroup(definition.Key, type.TypeArguments, type.TypeArguments, [], type.CanonicalName);
        return true;
    }

    private void ValidateGroup(EntityKey owner, ImmutableArray<CliTypeIdentity> arguments,
        ImmutableArray<CliTypeIdentity> typeArguments, ImmutableArray<CliTypeIdentity> methodArguments, string display)
    {
        var parameters = _parameters.Read(owner);
        if (parameters.Length != arguments.Length)
            throw Failure(display, "generic argument count does not match the declaration");
        for (var index = 0; index < parameters.Length; index++)
        {
            var argument = arguments[index];
            Validate(argument);
            var parameter = parameters[index];
            _specialConstraints.Validate(new(argument, parameter.Attributes, display, 0));
            foreach (var declared in parameter.Types)
            {
                var required = declared.Substitute(typeArguments, methodArguments);
                if (required.ContainsGenericParameters)
                    throw Failure(display, "a generic constraint could not be fully substituted");
                if (!_relationships.Classify(argument, required).IsAssignmentCompatible)
                    throw Failure(display, $"generic argument '{argument}' does not satisfy constraint '{required}'");
            }
        }
    }

    private static CompilerException Failure(string display, string reason) =>
        new(new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, $"'{display}': {reason}.", display));
}
