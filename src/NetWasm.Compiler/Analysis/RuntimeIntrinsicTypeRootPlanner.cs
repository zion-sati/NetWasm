// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class RuntimeIntrinsicTypeRootPlanner(
    IRuntimeIntrinsicRegistry intrinsics,
    INullableTypeResolver nullableTypes,
    ITypeDefinitionResolver typeDefinitions,
    IArrayCreationRootPlanner arrayCreation,
    IStructureMarshallingClosure structureClosure,
    ISharedArrayShapeResolver sharedArrays) : IRuntimeIntrinsicTypeRootPlanner
{
    private readonly ISharedArrayShapeResolver _sharedArrays = sharedArrays ??
        throw new ArgumentNullException(nameof(sharedArrays));
    private readonly IStructureMarshallingClosure _structureClosure = structureClosure ??
        throw new ArgumentNullException(nameof(structureClosure));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));

    private readonly IArrayCreationRootPlanner _arrayCreation = arrayCreation ??
        throw new ArgumentNullException(nameof(arrayCreation));

    private static readonly System.Collections.Immutable.ImmutableArray<int> RectangularCreationRanks = [2, 3];
    // With lower bounds, a rank-one array is T[*]; MakeArrayType(rank) makes the same ranks.
    private static readonly System.Collections.Immutable.ImmutableArray<int> BoundedCreationRanks = [1, 2, 3];

    public RuntimeIntrinsicTypeRootPlan Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<CliTypeIdentity> reachableTypes,
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> typeHandleSeeds = default,
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> programAllocatedTypes = default)
    {
        ArgumentNullException.ThrowIfNull(reachableMethods);
        ArgumentNullException.ThrowIfNull(reachableTypes);

        var demands = reachableMethods
            .Select(method => (Method: method, Intrinsic: _intrinsics.TryGetIntrinsic(
                method.Definition.Key, out var intrinsic) ? intrinsic : (RuntimeIntrinsic?)null))
            .ToArray();
        var inspectNullable = demands.Any(demand =>
            demand.Intrinsic is RuntimeIntrinsic.NullableGetUnderlyingType or RuntimeIntrinsic.ArraySetValue);
        var createArrays = demands.Any(demand => demand.Intrinsic == RuntimeIntrinsic.ArrayCreateInstance);
        var createRectangularArrays = demands.Any(demand =>
            demand.Intrinsic == RuntimeIntrinsic.ArrayCreateRectangularInstance);
        var createBoundedArrays = demands.Any(demand =>
            demand.Intrinsic == RuntimeIntrinsic.ArrayCreateBoundedInstance);
        var createSharedArrays = demands.Any(demand =>
            demand.Intrinsic == RuntimeIntrinsic.ArrayCreateSharedInstance);
        var makeVectorTypes = demands.Any(demand => ReachabilityMarkers.MakesArrayTypes(demand.Method) &&
            demand.Method.Definition.Signature.ParameterTypes.IsEmpty);
        var makeRankedTypes = demands.Any(demand => ReachabilityMarkers.MakesArrayTypes(demand.Method) &&
            !demand.Method.Definition.Signature.ParameterTypes.IsEmpty);
        var runClassConstructors = demands.Any(demand =>
            demand.Intrinsic == RuntimeIntrinsic.RunClassConstructor);
        var allocateUninitialized = demands.Any(demand =>
            demand.Intrinsic == RuntimeIntrinsic.AllocateUninitializedObject);
        var marshalStructures = demands.Any(demand => demand.Intrinsic is
            RuntimeIntrinsic.StructMarshallingData or RuntimeIntrinsic.StructMarshallingFieldNames);
        var enumNames = demands.Any(demand => demand.Intrinsic == RuntimeIntrinsic.EnumGetNames);
        var boxEnums = demands.Any(demand => demand.Intrinsic == RuntimeIntrinsic.EnumToObject);
        var boxArrayElements = demands.Any(demand =>
            demand.Intrinsic == RuntimeIntrinsic.ArrayGetValue);
        var enumValues = demands.Where(demand => demand.Intrinsic == RuntimeIntrinsic.EnumGetValues)
            .Select(demand => demand.Method).ToArray();
        if (!createArrays && !createRectangularArrays && !createBoundedArrays && !createSharedArrays && !makeVectorTypes &&
            !makeRankedTypes &&
            !runClassConstructors && !allocateUninitialized &&
            !marshalStructures && !inspectNullable && !enumNames && !boxEnums && !boxArrayElements &&
            enumValues.Length == 0)
        {
            return RuntimeIntrinsicTypeRootPlan.Empty;
        }

        var runtimeTypes = new HashSet<CliTypeIdentity>();
        var allocatedTypes = new HashSet<CliTypeIdentity>();
        var types = reachableTypes.Distinct().ToArray();
        // An array of a reference element the program has no array type of is a shared array (SharedArrayShapes): one
        // type per shape, whatever elements the program passes. An array of a value element needs its own type, so
        // CreateInstance gets one for the value types the program names by token (typeof) and no others, as Native AOT
        // creates only the arrays it compiled; any other value element throws NotSupportedException.
        // CreateInstance allocates the arrays of value elements it creates, so the program's own value arrays of the
        // shape, whose types it compiles anyway, are allocated with them. A value type is named by a token for it or
        // as a type argument of one (typeof(Point?), typeof(IEnumerable<Point>)).
        var handles = typeHandleSeeds.IsDefault ? [] : typeHandleSeeds;
        var namedValues = handles.Concat(handles.SelectMany(type => type.TypeArguments)).Where(IsValueElement)
            .Distinct().ToArray();
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> ValueSeeds(CliTypeShape shape) =>
        [
            .. namedValues,
            .. types.Where(type => type.Shape == shape).Select(type => type.ElementType!).Where(IsValueElement),
        ];
        if (createArrays)
        {
            var creation = _arrayCreation.Plan(ValueSeeds(CliTypeShape.SzArray));
            runtimeTypes.UnionWith(creation.RuntimeTypes);
            allocatedTypes.UnionWith(creation.AllocatedTypes);
        }
        if (createRectangularArrays || createBoundedArrays)
        {
            // The fixed-rank Array.CreateInstance overloads create rank 2 and 3; other ranks
            // use rectangular array types the program constructs itself.
            var creation = _arrayCreation.Plan(ValueSeeds(CliTypeShape.Array),
                createBoundedArrays ? BoundedCreationRanks : RectangularCreationRanks);
            runtimeTypes.UnionWith(creation.RuntimeTypes);
            allocatedTypes.UnionWith(creation.AllocatedTypes);
        }
        if (createSharedArrays)
        {
            AddShared(createArrays, allocated: true, ranks: []);
            AddShared(false, allocated: true,
                ranks: createBoundedArrays ? BoundedCreationRanks : createRectangularArrays ? RectangularCreationRanks : []);
        }
        // MakeArrayType returns a type and allocates nothing: a type of its own for an element the program has the
        // array type of, else the shared shape's for the element (CoreLib's SharedArrays).
        AddShared(makeVectorTypes, allocated: false, ranks: makeRankedTypes ? BoundedCreationRanks : []);

        void AddShared(bool vector, bool allocated, System.Collections.Immutable.ImmutableArray<int> ranks)
        {
            foreach (var shape in (vector ? [_sharedArrays.Resolve(0)] : System.Linq.Enumerable.Empty<CliTypeIdentity>())
                .Concat(ranks.Select(_sharedArrays.Resolve)))
            {
                runtimeTypes.Add(shape);
                if (allocated) allocatedTypes.Add(shape);
            }
        }
        if (boxArrayElements)
        {
            foreach (var array in types.Where(type => type.Shape is CliTypeShape.SzArray or CliTypeShape.Array))
            {
                var element = _nullableTypes.Resolve(array.ElementType!) ?? array.ElementType!;
                if (!element.IsValueType || element.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer)
                {
                    continue;
                }
                runtimeTypes.Add(element);
                allocatedTypes.Add(element);
            }
        }
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

        // Like native AOT, RunClassConstructor compiles the initializers of types whose handles
        // the program names directly (type tokens and type operands); other types keep only the
        // initializers their own static accesses require.
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> initializedTypes =
            runClassConstructors && !typeHandleSeeds.IsDefault
                ? [.. typeHandleSeeds
                    .Where(type => !type.ContainsGenericParameters &&
                        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation)
                    .Distinct()
                    .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)]
                : [];
        var seeds = typeHandleSeeds.IsDefault ? [] : typeHandleSeeds;
        var programAllocated = programAllocatedTypes.IsDefault ? [] : programAllocatedTypes;
        // GetUninitializedObject creates the types the program names or already allocates, as
        // native AOT creates only types it has seen constructed.
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> uninitializedObjectTypes =
            allocateUninitialized
                ? [.. seeds.Concat(programAllocated)
                    .Select(UninitializedObjectType)
                    .OfType<CliTypeIdentity>()
                    .Distinct()
                    .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)]
                : [];
        // Marshal's structure members receive the types the program names, boxed values of the
        // value types it allocates, and the formatted classes (sequential or explicit) it allocates.
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> structMarshallingTypes =
            marshalStructures
                ? [.. seeds.Where(IsStructMarshallingCandidate)
                    .Concat(programAllocated.Where(type =>
                        (type.IsValueType || IsFormattedClass(type)) && IsStructMarshallingCandidate(type)))
                    .Distinct()
                    .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)]
                : [];
        // Their fields reach embedded formatted classes and the structures of ByValArray arrays, which have
        // descriptors of their own and which PtrToStructure creates: the classes, and each element's boxed value as
        // it fills the element's array.
        if (marshalStructures)
        {
            var reach = _structureClosure.Reach(structMarshallingTypes);
            structMarshallingTypes = [.. structMarshallingTypes.Concat(reach.Types)
                .Distinct()
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)];
            uninitializedObjectTypes = [.. uninitializedObjectTypes.Concat(reach.EmbeddedClasses).Concat(reach.ArrayElements)
                .Distinct()
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)];
            foreach (var element in reach.ArrayElements)
            {
                AddArray(element);
                allocatedTypes.Add(element);
            }
            foreach (var element in reach.EnumArrayElements)
            {
                AddArray(element);
            }
        }
        return runtimeTypes.Count == 0 && allocatedTypes.Count == 0 && initializedTypes.IsEmpty &&
            uninitializedObjectTypes.IsEmpty && !marshalStructures
            ? RuntimeIntrinsicTypeRootPlan.Empty
            : new(
            [.. runtimeTypes.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            [.. allocatedTypes.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)])
            {
                InitializedTypes = initializedTypes,
                UninitializedObjectTypes = uninitializedObjectTypes,
                StructMarshallingTypes = structMarshallingTypes,
                StructMarshallingFieldNames = demands.Any(demand =>
                    demand.Intrinsic == RuntimeIntrinsic.StructMarshallingFieldNames),
                StructMarshallingConstructors = marshalStructures && demands.Any(demand =>
                    demand.Intrinsic == RuntimeIntrinsic.StructMarshallingConstruct),
            };

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

    // Upstream rejects abstract types, interfaces, strings, arrays, pointers, byref-like and open
    // types before allocating, and creates T for Nullable<T>.
    private CliTypeIdentity? UninitializedObjectType(CliTypeIdentity type)
    {
        if (type.ContainsGenericParameters)
            return null;
        type = _nullableTypes.Resolve(type) ?? type;
        switch (type.Shape)
        {
            case CliTypeShape.Primitive:
                return type.CanonicalName is "primitive:void" or "primitive:string" or "primitive:typedref"
                    ? null : type;
            case CliTypeShape.Named:
            case CliTypeShape.GenericInstantiation:
                var definition = _typeDefinitions.ResolveTypeIdentity(type);
                return definition.IsInterface || definition.IsAbstract || definition.IsByRefLike ||
                    type.Shape == CliTypeShape.Named && definition.GenericArity != 0
                    ? null : type;
            default:
                return null;
        }
    }

    // Upstream rejects generic types before reading interop data, so only non-generic named types
    // and the primitive value types can carry a descriptor.
    private static bool IsValueElement(CliTypeIdentity type) => type.StackKind != CliValueKind.ManagedReference;

    private static bool IsStructMarshallingCandidate(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.Named && !type.ContainsGenericParameters ||
        type.Shape == CliTypeShape.Primitive && type.IsValueType &&
            type.CanonicalName is not ("primitive:void" or "primitive:typedref");

    private bool IsFormattedClass(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.Named && !type.ContainsGenericParameters &&
        _typeDefinitions.ResolveTypeIdentity(type) is { IsInterface: false, LayoutKind: not CliTypeLayoutKind.Auto };

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
