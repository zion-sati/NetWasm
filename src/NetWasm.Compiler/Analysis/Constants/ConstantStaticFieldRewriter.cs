using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// A static readonly field is a compile-time constant where its owner's static
// constructor stores a constant in it before running anything else and never
// stores it again: nothing may write it once the type is initialized, and no
// code can read it before that store. Its load becomes the constant, which
// later folding sees, as .NET's JIT reads such a field. A primitive or enum
// field reads the constant as its width stores it, and a reference field left
// null reads null.
// Where the constructor does nothing else, the owner's fields hold their
// constants, or zero, from the start and running it has no effect, so the load
// needs no initialization check, as BitConverter.IsLittleEndian in
// CharUnicodeInfo's table lookups. Where it does more, as a later initializer
// that calls a method, the load keeps the owner's initialization, the field's
// address dropped as BeforeFieldInitEntryInitializer writes one, so the
// constructor still runs, and fails, where .NET's would.
internal sealed class ConstantStaticFieldRewriter(
    IFieldRepository fields,
    ITypeIdentityResolver typeIdentities,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    ICalleeMethodBodyReader methodBodies) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<EntityKey, Initialization> _initializers = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<CilInstruction>[]? replacements = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            // A volatile load keeps its memory semantics.
            if (instruction.Operation != CilOperation.LoadStaticField ||
                index > 0 && body.Instructions[index - 1].Operation == CilOperation.Volatile ||
                Constant(fields.GetField(CilOperands.FieldKey(instruction.Operand)), out var initializes) is not { } constant)
            {
                continue;
            }
            replacements ??= [.. body.Instructions.Select(original => new List<CilInstruction> { original })];
            replacements[index] = initializes
                ? [instruction with { Operation = CilOperation.LoadStaticFieldAddress }, Load(CilOperation.Pop, new CilOperand.None()), constant]
                : [constant];
        }
        return replacements is null ? body : RenumberedInstructions.Splice(body, replacements);
    }

    // The load of the constant the field holds, as its type reads back what was
    // stored, and whether reading it must still initialize its owner, or null.
    private CilInstruction? Constant(FieldDefinitionModel field, out bool initializes)
    {
        initializes = false;
        if (!field.IsStatic || !field.IsInitOnly || field.IsLiteral || !field.InitialData.IsDefaultOrEmpty)
        {
            return null;
        }
        if (StaticFieldConstantLoads.Storage(field.SignatureType, typeDefinitions) is not { } storage) return null;
        var (reference, type) = storage;
        var owner = typeDefinitions.ResolveTypeIdentity(typeIdentities.GetTypeIdentity(field.DeclaringType));
        var initialization = _initializers.GetOrAdd(owner.Key, _ => Initializer(owner));
        // A field the constructor does not store first holds zero only where it does nothing else.
        if (!initialization.Constants.TryGetValue(field.Key, out var value))
        {
            if (!initialization.Complete) return null;
            value = new(StaticFieldConstantLoads.StackKind(reference, type), 0, 0);
        }
        initializes = !initialization.Complete;
        return StaticFieldConstantLoads.Load(reference, type, value.Kind, value.Integer, value.Real);
    }

    // The constants the static constructor stores in the owner's static fields
    // before anything else runs, less any field it stores or addresses again,
    // and whether that is all it does. No constructor stores nothing.
    private Initialization Initializer(TypeDefinitionModel owner)
    {
        var constructor = owner.Methods.Select(methods.GetMethod).SingleOrDefault(method => method.Name == ".cctor");
        if (constructor is null) return new([], Complete: true);
        // A constructor that cannot be decoded stores nothing this rewrite can know of.
        if (!methodBodies.TryReadMethodBody(constructor, out var body)) return new([], Complete: false);
        var operations = body.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
        var stored = new Dictionary<EntityKey, Stored>();
        var pending = (Stored?)null;
        var leading = 0;
        while (leading < operations.Length && Leading(operations[leading])) leading++;
        // A constant waiting for its store belongs to what follows.
        if (pending is not null) leading--;
        foreach (var operation in operations[leading..])
        {
            if (operation.Operation is CilOperation.StoreStaticField or CilOperation.LoadStaticFieldAddress)
            {
                stored.Remove(CilOperands.FieldKey(operation.Operand));
            }
        }
        return new(stored, operations.Length != 0 && leading == operations.Length - 1 &&
            operations[^1].Operation == CilOperation.Return && body.ExceptionRegions.IsEmpty);

        // A constant load, or its store to the owner's own static field: neither runs anything else.
        bool Leading(CilInstruction operation)
        {
            switch (operation)
            {
                case { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 constant } when pending is null:
                    pending = new(CliValueKind.I4, constant.Value, 0);
                    return true;
                case { Operation: CilOperation.LoadInt64, Operand: CilOperand.ConstantI8 constant } when pending is null:
                    pending = new(CliValueKind.I8, constant.Value, 0);
                    return true;
                // Roslyn writes a small long as an int and a widening.
                case { Operation: CilOperation.ConvertInt64 } when pending is { Kind: CliValueKind.I4 } value:
                    pending = value with { Kind = CliValueKind.I8 };
                    return true;
                case { Operation: CilOperation.ConvertInt64Unsigned } when pending is { Kind: CliValueKind.I4 } value:
                    pending = new(CliValueKind.I8, (uint)value.Integer, 0);
                    return true;
                // The evaluation stack holds every float as F, whatever its width.
                case { Operation: CilOperation.LoadFloat32, Operand: CilOperand.ConstantF4 constant } when pending is null:
                    pending = new(CliValueKind.F8, 0, constant.Value);
                    return true;
                case { Operation: CilOperation.LoadFloat64, Operand: CilOperand.ConstantF8 constant } when pending is null:
                    pending = new(CliValueKind.F8, 0, constant.Value);
                    return true;
                case { Operation: CilOperation.LoadNull } when pending is null:
                    pending = new(CliValueKind.ManagedReference, 0, 0);
                    return true;
                case { Operation: CilOperation.StoreStaticField } when pending is { } value &&
                    fields.GetField(CilOperands.FieldKey(operation.Operand)) is var target &&
                    target.DeclaringType == owner.Key:
                    stored[target.Key] = value;
                    pending = null;
                    return true;
                default:
                    return false;
            }
        }
    }

    private static CilInstruction Load(CilOperation operation, CilOperand operand) =>
        StaticFieldConstantLoads.Instruction(operation, operand);

    // A constant as the evaluation stack holds it.
    private readonly record struct Stored(CliValueKind Kind, long Integer, double Real);

    private sealed record Initialization(Dictionary<EntityKey, Stored> Constants, bool Complete);
}
