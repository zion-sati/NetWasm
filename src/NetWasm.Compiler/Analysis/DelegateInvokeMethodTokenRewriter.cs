using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// DelegateMetadata.InvokeMethodHandle<TDelegate>() becomes the method token of TDelegate's Invoke,
// so a delegate type carries Invoke metadata only where code names it when compiling. A type
// argument that is not a delegate type calls ThrowNotDelegate instead.
internal sealed class DelegateInvokeMethodTokenRewriter(
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    internal const string MetadataType = "System.Runtime.CompilerServices.DelegateMetadata";

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToBuilder();
        var changed = false;
        for (var index = 0; index < instructions.Count; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { MethodArguments: [var delegateType] } target ||
                target.Definition.Name != "InvokeMethodHandle" ||
                symbols.Format(target.Definition.DeclaringType) != MetadataType)
            {
                continue;
            }
            instructions[index] = InvokeMethod(delegateType) is { } invoke
                ? call with { Operation = CilOperation.LoadMethodToken, Operand = new CilOperand.MethodInstance(invoke) }
                : call with { Operand = new CilOperand.MethodInstance(Thrower(target)) };
            changed = true;
        }
        return changed ? body with { Instructions = instructions.ToImmutable() } : body;
    }

    private MethodInstanceModel? InvokeMethod(CliTypeIdentity type)
    {
        if (type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) ||
            type.ContainsGenericParameters)
        {
            return null;
        }
        var definition = typeDefinitions.ResolveTypeIdentity(type);
        if (definition.FullName is "System.Delegate" or "System.MulticastDelegate" ||
            definition.Methods.Select(methods.GetMethod)
                .Where(method => method.Name == "Invoke" && !method.IsStatic).Take(2).ToArray() is not [var invoke])
        {
            return null;
        }
        return methodInstances.ResolveMethodInstance(
            invoke.Key.Assembly,
            invoke.Key.MetadataToken,
            symbols.Format(invoke),
            0,
            new CliGenericContext(type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [], []));
    }

    private MethodInstanceModel Thrower(MethodInstanceModel handle)
    {
        var thrower = typeDefinitions.ResolveTypeIdentity(handle.DeclaringType).Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == "ThrowNotDelegate");
        return methodInstances.ResolveMethodInstance(
            thrower.Key.Assembly, thrower.Key.MetadataToken, symbols.Format(thrower), 0);
    }
}
