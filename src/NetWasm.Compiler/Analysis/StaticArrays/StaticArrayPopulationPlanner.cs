// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal sealed class StaticArrayPopulationPlanner(
    IFieldRepository fields,
    ICalledMethodResolver calls,
    ITypeOperandResolver types,
    IValueConstructorPurityClassifier constructors,
    IClosedTypeIdentityComparer identities,
    ITypeDefinitionResolver definitions,
    IValueTypeInitializationClassifier initialization) : IStaticArrayPopulationPlanner
{
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly ITypeOperandResolver _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IValueConstructorPurityClassifier _constructors = constructors ?? throw new ArgumentNullException(nameof(constructors));
    private readonly IClosedTypeIdentityComparer _identities = identities ?? throw new ArgumentNullException(nameof(identities));
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly IValueTypeInitializationClassifier _initialization = initialization ??
        throw new ArgumentNullException(nameof(initialization));

    public ImmutableHashSet<int> Plan(ManagedMethodBody method, ImmutableHashSet<EntityKey> observedFields)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(observedFields);
        if (method.Method.Definition.Name != ".cctor" || !method.Body.ExceptionRegions.IsEmpty) return [];
        var removed = ImmutableHashSet.CreateBuilder<int>();
        var constructorFacts = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var block in method.ControlFlow.Graph.Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 1; index < operations.Length; index++)
            {
                if (operations[index].Operation != CilOperation.NewArray ||
                    operations[index - 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: >= 0 } length }) continue;
                var element = _types.Resolve(operations[index], method.Method);
                var locals = new Dictionary<int, PopulationValue>();
                var end = PopulationEnd(method.Method, operations, index + 1, length.Value, element, constructorFacts, locals);
                if (end <= index + 1 || end >= operations.Length || operations[end].Operation != CilOperation.StoreStaticField) continue;
                var field = ResolveField(operations[end].Operand);
                if (field is not { Definition.IsStatic: true, Definition.IsInitOnly: true } ||
                    observedFields.Contains(field.Definition.Key) || field.Type.Shape != CliTypeShape.SzArray ||
                    _identities.Compare(field.Type.ElementType!, element) != true ||
                    !Confined(method.Body, operations[(index + 1)..end], locals.Keys)) continue;
                for (var remove = index + 1; remove < end; remove++) removed.Add(operations[remove].Offset);
                index = end;
            }
        }
        return removed.ToImmutable();
    }

    // The locals an inlined element constructor builds its value in belong to the population alone: nothing outside
    // it reads, writes or takes the address of one, so removing the population removes every use.
    private static bool Confined(CilMethodBody body, CilInstruction[] population, IEnumerable<int> locals)
    {
        var used = locals.ToHashSet();
        if (used.Count == 0) return true;
        var inside = population.Select(instruction => instruction.Offset).ToHashSet();
        return !body.Instructions.Any(instruction => !inside.Contains(instruction.Offset) &&
            instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress &&
            instruction.Operand is CilOperand.Index { Value: var index } && used.Contains(index));
    }

    private int PopulationEnd(MethodInstanceModel method, CilInstruction[] operations, int start, int length,
        CliTypeIdentity element, Dictionary<string, bool> constructorFacts, Dictionary<int, PopulationValue> locals)
    {
        var cursor = start;
        while (cursor < operations.Length && operations[cursor].Operation == CilOperation.Duplicate)
        {
            if (++cursor >= operations.Length || operations[cursor] is not
                { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 index } ||
                (uint)index.Value >= (uint)length) return -1;
            cursor++;
            var values = new List<PopulationValue>();
            while (cursor < operations.Length && operations[cursor].Operation is not
                (CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference))
            {
                if (!ReadProducer(operations[cursor++], method, values, constructorFacts, locals)) return -1;
            }
            if (cursor >= operations.Length || values.Count != 1 || !MatchesStore(operations[cursor], method, element, values[0])) return -1;
            cursor++;
        }
        return cursor;
    }

    private bool ReadProducer(CilInstruction instruction, MethodInstanceModel initializer,
        List<PopulationValue> values, Dictionary<string, bool> constructorFacts, Dictionary<int, PopulationValue> locals)
    {
        switch (instruction.Operation)
        {
            // An element constructor the inliner expanded: its arguments in locals, then the value built in a local
            // (ldloca; initobj; ldloca, value, stfld per field; ldloc), as the pure constructor below would build it.
            case CilOperation.StoreLocal when instruction.Operand is CilOperand.Index { Value: var stored }:
                if (values.Count == 0 || values[^1].Address is not null) return false;
                locals[stored] = values[^1];
                values.RemoveAt(values.Count - 1);
                return true;
            case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var loaded }:
                if (!locals.TryGetValue(loaded, out var local)) return false;
                values.Add(local);
                return true;
            case CilOperation.LoadLocalAddress when instruction.Operand is CilOperand.Index { Value: var addressed }:
                // A local the population has not built yet is one only initobj may start.
                locals.TryAdd(addressed, new(CliValueKind.Void));
                values.Add(new(CliValueKind.ManagedAddress, Address: addressed));
                return true;
            case CilOperation.InitializeObject:
                var built = _types.Resolve(instruction, initializer);
                if (values.Count == 0 || values[^1].Address is not { } target || !IsReferenceFreeScalarValue(built) ||
                    !_initialization.ConstructsWithoutInitialization(built)) return false;
                values.RemoveAt(values.Count - 1);
                locals[target] = new(CliValueKind.ValueType, built);
                return true;
            case CilOperation.StoreField:
                return StoreBuiltField(instruction, values, locals);
            case CilOperation.LoadInt32: values.Add(new(CliValueKind.I4)); return true;
            case CilOperation.LoadInt64: values.Add(new(CliValueKind.I8)); return true;
            case CilOperation.LoadFloat32: values.Add(new(CliValueKind.F4)); return true;
            case CilOperation.LoadFloat64: values.Add(new(CliValueKind.F8)); return true;
            case CilOperation.LoadNull: values.Add(new(CliValueKind.ManagedReference, IsNull: true)); return true;
            case CilOperation.LoadString: values.Add(new(CliValueKind.ManagedReference)); return true;
            case CilOperation.ConvertInt32:
            case CilOperation.ConvertInt32Unsigned:
            case CilOperation.ConvertInt64:
            case CilOperation.ConvertInt64Unsigned:
            case CilOperation.ConvertFloat32:
            case CilOperation.ConvertFloat64:
                if (values.Count == 0 || values[^1].Kind is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8)) return false;
                values[^1] = new(instruction.Operation switch
                {
                    CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned => CliValueKind.I4,
                    CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned => CliValueKind.I8,
                    CilOperation.ConvertFloat32 => CliValueKind.F4,
                    _ => CliValueKind.F8,
                });
                return true;
            case CilOperation.LoadStaticField:
                var field = ResolveField(instruction.Operand);
                if (field is not { Definition.IsStatic: true, Definition.IsInitOnly: true } ||
                    field.Definition.DeclaringType != initializer.Definition.DeclaringType ||
                    (field.Owner is null ? initializer.IsConstructed : !field.Owner.Equals(initializer.DeclaringType)) ||
                    !IsReferenceFreeScalarValue(field.Type)) return false;
                values.Add(new(field.Type.StackKind, field.Type));
                return true;
            case CilOperation.NewObject:
                var constructor = _calls.Resolve(instruction);
                if (constructor is null || values.Count < constructor.Signature.ParameterTypes.Length) return false;
                var count = constructor.Signature.ParameterTypes.Length;
                for (var index = 0; index < count; index++)
                {
                    if (values[values.Count - count + index].Kind != constructor.Signature.ParameterTypes[index]) return false;
                }
                if (!constructorFacts.TryGetValue(constructor.CanonicalName, out var pure))
                {
                    pure = _constructors.IsPure(constructor);
                    constructorFacts.Add(constructor.CanonicalName, pure);
                }
                if (!pure) return false;
                values.RemoveRange(values.Count - count, count);
                values.Add(new(CliValueKind.ValueType, constructor.DeclaringType));
                return true;
            default:
                return false;
        }
    }

    // A field store into a value the population is building: a scalar of the field's kind into an instance field of
    // the value's own type, through the local's address.
    private bool StoreBuiltField(CilInstruction instruction, List<PopulationValue> values, Dictionary<int, PopulationValue> locals)
    {
        // Only a local's address carries an Address, and its kind is a managed address.
        if (values.Count < 2 || values[^1].Kind is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8) ||
            values[^2].Address is not { } target || !locals.TryGetValue(target, out var built) ||
            built is not { Kind: CliValueKind.ValueType, Type: { } type }) return false;
        var field = instruction.Operand switch
        {
            CilOperand.Entity entity when type.Shape != CliTypeShape.GenericInstantiation => _fields.GetField(entity.Key),
            CilOperand.FieldInstance instance when instance.Value.DeclaringType.Equals(type) => instance.Value.Definition,
            _ => null,
        };
        if (field is null || field.IsStatic || field.DeclaringType != _definitions.ResolveTypeIdentity(type).Key ||
            field.FieldType != values[^1].Kind) return false;
        values.RemoveRange(values.Count - 2, 2);
        return true;
    }

    private bool MatchesStore(CilInstruction store, MethodInstanceModel method, CliTypeIdentity element, PopulationValue value)
    {
        if (store.Operation == CilOperation.StoreArrayElementReference)
        {
            // The only accepted reference producers are null and string literals.
            return !element.IsValueType && value.Kind == CliValueKind.ManagedReference &&
                (value.IsNull || _identities.Compare(element, CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false)) == true);
        }
        if (_identities.Compare(_types.Resolve(store, method), element) != true || value.Kind != element.StackKind) return false;
        // Both struct producers (a field and a constructor) supply a closed type.
        return value.Kind != CliValueKind.ValueType || _identities.Compare(value.Type!, element) == true;
    }

    private PopulationField? ResolveField(CilOperand operand) => operand switch
    {
        CilOperand.FieldInstance field => new(field.Value.Definition, field.Value.FieldType, field.Value.DeclaringType),
        CilOperand.Entity entity => FromDefinition(_fields.GetField(entity.Key)),
        _ => null,
    };

    private static PopulationField FromDefinition(FieldDefinitionModel field) => new(field, field.SignatureType, null);

    private bool IsReferenceFreeScalarValue(CliTypeIdentity type) => type.StackKind is
        CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt ||
        type.StackKind == CliValueKind.ValueType && _definitions.ResolveTypeIdentity(type).Fields.All(key =>
        {
            var field = _fields.GetField(key);
            return field.IsStatic || field.FieldType is
                CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt;
        });

    private sealed record PopulationField(FieldDefinitionModel Definition, CliTypeIdentity Type, CliTypeIdentity? Owner);
    // Address: the local whose address the value is, for a value the population builds in a local.
    private sealed record PopulationValue(CliValueKind Kind, CliTypeIdentity? Type = null, bool IsNull = false, int? Address = null);
}
