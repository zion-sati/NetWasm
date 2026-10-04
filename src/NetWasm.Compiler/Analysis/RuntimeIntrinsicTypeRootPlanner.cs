using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class RuntimeIntrinsicTypeRootPlanner(
    IRuntimeIntrinsicRegistry intrinsics,
    INullableTypeResolver nullableTypes,
    ITypeDefinitionResolver typeDefinitions) : IRuntimeIntrinsicTypeRootPlanner
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));

    public RuntimeIntrinsicTypeRootPlan Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<CliTypeIdentity> reachableTypes)
    {
        ArgumentNullException.ThrowIfNull(reachableMethods);
        ArgumentNullException.ThrowIfNull(reachableTypes);

        var demands = reachableMethods
            .Select(method => (Method: method, Intrinsic: _intrinsics.TryGetIntrinsic(
                method.Definition.Key, out var intrinsic) ? intrinsic : (RuntimeIntrinsic?)null))
            .ToArray();
        var inspectNullable = demands.Any(demand =>
            demand.Intrinsic == RuntimeIntrinsic.NullableGetUnderlyingType);
        var enumNames = demands.Any(demand => demand.Intrinsic == RuntimeIntrinsic.EnumGetNames);
        var boxEnums = demands.Any(demand => demand.Intrinsic == RuntimeIntrinsic.EnumToObject);
        var enumValues = demands.Where(demand => demand.Intrinsic == RuntimeIntrinsic.EnumGetValues)
            .Select(demand => demand.Method).ToArray();
        if (!inspectNullable && !enumNames && !boxEnums && enumValues.Length == 0)
        {
            return RuntimeIntrinsicTypeRootPlan.Empty;
        }

        var runtimeTypes = new HashSet<CliTypeIdentity>();
        var allocatedTypes = new HashSet<CliTypeIdentity>();
        var types = reachableTypes.Distinct().ToArray();
        var boxArrayElements = demands.Any(demand => demand.Intrinsic == RuntimeIntrinsic.ArrayGetValue);
        if (inspectNullable)
        {
            runtimeTypes.UnionWith(types.Select(_nullableTypes.Resolve).OfType<CliTypeIdentity>());
        }
        if (enumNames)
        {
            AddArray(CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false));
        }
        var typeBasedValues = enumValues.Any(method => method.MethodArguments.IsEmpty);
        if (typeBasedValues || boxEnums)
        {
            foreach (var type in types)
            {
                if (ResolveEnum(type) is { } definition)
                {
                    if (type.Shape == CliTypeShape.GenericInstantiation || definition.GenericArity == 0)
                    {
                        if (boxEnums)
                        {
                            runtimeTypes.Add(type);
                            allocatedTypes.Add(type);
                        }
                        if (typeBasedValues)
                        {
                            AddArray(type);
                        }
                    }
                    // An open enum definition still exposes its concrete underlying
                    // array, although it cannot supply enum instance storage.
                    if (typeBasedValues)
                    {
                        AddArray(definition.EnumUnderlyingType);
                    }
                }
            }
        }
        foreach (var method in enumValues.Where(method => !method.MethodArguments.IsEmpty))
        {
            if (method.MethodArguments.Length != 1 ||
                ResolveEnum(method.MethodArguments[0]) is not { } definition ||
                method.MethodArguments[0].Shape == CliTypeShape.Named && definition.GenericArity != 0)
            {
                throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.RuntimeContract,
                    "Enum array construction requires one closed enum type argument.", method.CanonicalName));
            }
            AddArray(method.Definition.Name == "InternalGetValuesAsUnderlyingType"
                ? definition.EnumUnderlyingType
                : method.MethodArguments[0]);
        }

        return new(
            [.. runtimeTypes.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            [.. allocatedTypes.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)]);

        void AddArray(CliTypeIdentity element)
        {
            var array = CliTypeIdentity.SzArray(element);
            runtimeTypes.Add(element);
            runtimeTypes.Add(array);
            allocatedTypes.Add(array);
            if (boxArrayElements && element.IsValueType)
            {
                allocatedTypes.Add(element);
            }
        }
    }

    private TypeDefinitionModel? ResolveEnum(CliTypeIdentity type)
    {
        if (!type.IsValueType || type.ContainsGenericParameters ||
            type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
        {
            return null;
        }
        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        return definition.IsEnum ? definition : null;
    }
}
