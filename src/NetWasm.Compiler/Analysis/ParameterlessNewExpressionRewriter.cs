using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// C# builds `new C()` of a class, in an expression tree, as Expression.New(typeof(C)): ldtoken C,
// Type.GetTypeFromHandle, then New(Type), which finds C's parameterless constructor by reflection. With C known
// here, the three instructions become the constructor's handle, C's handle and Expression.NewParameterless, so
// New gets the constructor, as C# passes it for `new C(args)`. A value type keeps New(Type), which NetWasm runs
// without a lookup; an abstract class, an interface and a class without a parameterless constructor keep it too,
// and throw as before.
internal sealed class ParameterlessNewExpressionRewriter(
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    ITypeFinder types) : IMethodRewriteRule
{
    private MethodInstanceModel? _helper;

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 2; index < instructions.Length; index++)
        {
            var newCall = instructions[index];
            var typeOf = instructions[index - 1];
            var token = instructions[index - 2];
            if (token is not { Operation: CilOperation.LoadTypeToken, Operand: CilOperand.TypeIdentity { Value: var created } } ||
                !IsCall(typeOf, "System.Type", "GetTypeFromHandle") ||
                !IsCall(newCall, "System.Linq.Expressions.Expression", "New") ||
                calledMethods.Resolve(newCall)!.Signature.ParameterSignatureTypes is not [{ FullName: "System.Type" }] ||
                Constructor(created) is not { } constructor)
            {
                continue;
            }
            instructions[index - 2] = token with
            {
                Operation = CilOperation.LoadMethodToken,
                Operand = new CilOperand.MethodInstance(constructor),
            };
            // The class's handle selects the constructor's instantiation, as GetMethodFromHandle(method, type) does.
            instructions[index - 1] = typeOf with
            {
                Operation = CilOperation.LoadTypeToken,
                Operand = new CilOperand.TypeIdentity(created),
            };
            instructions[index] = newCall with { Operand = new CilOperand.MethodInstance(Helper()) };
            changed = true;
        }
        // Both handles are on the stack before the call, one more value than the type handle was.
        return changed ? body with { Instructions = [.. instructions], MaxStack = body.MaxStack + 1 } : body;
    }

    private bool IsCall(CilInstruction instruction, string typeName, string methodName) =>
        instruction.Operation == CilOperation.Call &&
        calledMethods.Resolve(instruction) is { } method &&
        method.Definition.IsStatic &&
        method.Definition.Name == methodName &&
        symbols.Format(method.Definition.DeclaringType) == typeName;

    private MethodInstanceModel? Constructor(CliTypeIdentity type)
    {
        if (type.IsValueType || type.ContainsGenericParameters ||
            type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation or CliTypeShape.Primitive) ||
            type.CanonicalName == "primitive:string")
        {
            return null;
        }
        // An interface has no constructor, so the search below leaves it alone too.
        var definition = typeDefinitions.ResolveTypeIdentity(type);
        if (definition.IsAbstract)
        {
            return null;
        }
        var constructor = definition.Methods
            .Select(methods.GetMethod)
            .SingleOrDefault(candidate =>
                candidate.Name == ".ctor" && !candidate.IsStatic && candidate.Signature.ParameterTypes.IsEmpty);
        return constructor is null ? null : methodInstances.ResolveMethodInstance(
            constructor.Key.Assembly,
            constructor.Key.MetadataToken,
            symbols.Format(constructor),
            0,
            new CliGenericContext(type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [], []));
    }

    private MethodInstanceModel Helper()
    {
        if (_helper is not null)
        {
            return _helper;
        }
        var helpers = types.FindType("System.Linq.Expressions.Expression").Methods
            .Select(methods.GetMethod)
            .Where(method => method.IsStatic && method.Name == "NewParameterless")
            .Take(2)
            .ToArray();
        if (helpers.Length != 1)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "expression trees must define exactly one 'Expression.NewParameterless'"));
        }
        var helper = helpers[0];
        return _helper = methodInstances.ResolveMethodInstance(
            helper.Key.Assembly, helper.Key.MetadataToken, symbols.Format(helper), 0);
    }
}
