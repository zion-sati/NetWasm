using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core.Types;

public sealed class StaticFieldInitializationClassifier(ITypeRepository types, IFieldRepository fields) :
    IStaticFieldInitializationClassifier
{
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public bool IsInitializedFor(
        MethodDefinitionModel accessor,
        CliTypeIdentity? accessorDeclaringType,
        ImmutableArray<CilInstruction> body,
        CilInstruction access)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(access);
        if (!IsStaticFieldAccess(access))
            return false;
        var owner = Owner(access, _fields);
        return IsOwnInstanceMethod(accessor, accessorDeclaringType, owner) ||
            // An access at the body's entry runs before everything else in it,
            // and when its check fails the method leaves before any other access.
            EntryAccesses(body).Any(entry => entry.Offset != access.Offset && Owner(entry, _fields).Initializes(owner));
    }

    // ECMA-335 I.8.9.5: a precise type initializes before any of its
    // constructors runs, and an instance of a reference type exists only after
    // one of them ran. So an instance method of such a type finds its own type
    // initialized, or initializing on this thread. A value type's instance
    // methods run on default instances that no constructor made, and a
    // BeforeFieldInit type initializes on field access only, so both keep
    // their checks.
    private bool IsOwnInstanceMethod(
        MethodDefinitionModel accessor,
        CliTypeIdentity? accessorDeclaringType,
        StaticFieldOwner owner)
    {
        if (accessor.IsStatic || accessor.DeclaringType != owner.Type)
            return false;
        var definition = _types.GetTypeDefinition(owner.Type);
        return !definition.IsBeforeFieldInit && !definition.IsValueType &&
            new StaticFieldOwner(accessor.DeclaringType, accessorDeclaringType).Initializes(owner);
    }

    public static bool IsStaticFieldAccess(CilInstruction instruction) =>
        instruction.Operation is CilOperation.LoadStaticField or CilOperation.StoreStaticField or
            CilOperation.LoadStaticFieldAddress &&
        instruction.Operand is CilOperand.FieldInstance or CilOperand.Entity;

    public static StaticFieldOwner Owner(CilInstruction access, IFieldRepository fields) => access.Operand switch
    {
        CilOperand.FieldInstance field => new(field.Value.Definition.DeclaringType, field.Value.DeclaringType),
        CilOperand.Entity entity => new(fields.GetField(entity.Key).DeclaringType, null),
        _ => throw new ArgumentException("The instruction does not access a static field.", nameof(access)),
    };

    // The field addresses a body takes and discards before anything else, which
    // check their types' initialization on entry.
    public static ImmutableArray<CilInstruction> EntryAccesses(ImmutableArray<CilInstruction> body)
    {
        var accesses = ImmutableArray.CreateBuilder<CilInstruction>();
        for (var index = 0; index < body.Length - 1 &&
             body[index].Operation == CilOperation.LoadStaticFieldAddress && IsStaticFieldAccess(body[index]) &&
             body[index + 1].Operation == CilOperation.Pop; index += 2)
        {
            accesses.Add(body[index]);
        }
        return accesses.ToImmutable();
    }
}
