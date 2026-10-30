using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// RuntimeHelpers.IsReferenceOrContainsReferences<T>() for a closed T is a constant. Folding it
// here, before reachability, lets ConstantBranchRewriter drop the branch it guards, so a callee
// such as upstream's ThrowArgument_TypeContainsReferences in Span<T>(void*, int) is not kept
// alive for element types that never take it. The answer follows ValueLayoutResolver: reference
// types and byrefs contain references, primitives and pointers do not, and a value type does
// when one of its instance fields does.
internal sealed class ClosedTypeContainsReferencesCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    ITypeDefinitionResolver types,
    IFieldRepository fields) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<CliTypeIdentity, bool> _answers = [];

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { MethodArguments: [var type] } target ||
                type.ContainsGenericParameters ||
                target.Definition.Name != "IsReferenceOrContainsReferences" ||
                symbols.Format(target.Definition.DeclaringType) != "System.Runtime.CompilerServices.RuntimeHelpers")
            {
                continue;
            }

            instructions[index] = call with
            {
                Operation = CilOperation.LoadInt32,
                Operand = new CilOperand.ConstantI4(ContainsReferences(type, []) ? 1 : 0),
            };
            changed = true;
        }

        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private bool ContainsReferences(CliTypeIdentity type, HashSet<CliTypeIdentity> active)
    {
        if (type.Shape == CliTypeShape.Modified)
        {
            return ContainsReferences(type.ElementType!, active);
        }
        if (!type.IsValueType || type.Shape == CliTypeShape.ManagedByReference)
        {
            return true;
        }
        if (type.Shape is CliTypeShape.Primitive or CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer)
        {
            return false;
        }
        if (_answers.TryGetValue(type, out var known))
        {
            return known;
        }
        if (!active.Add(type))
        {
            throw new InvalidOperationException($"Value type '{type}' contains itself.");
        }

        var typeArguments = type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [];
        var answer = types.ResolveTypeIdentity(type).Fields
            .Select(fields.GetField)
            .Where(field => !field.IsStatic)
            .Any(field => ContainsReferences(field.SignatureType.Substitute(typeArguments), active));
        active.Remove(type);
        _answers[type] = answer;
        return answer;
    }
}
