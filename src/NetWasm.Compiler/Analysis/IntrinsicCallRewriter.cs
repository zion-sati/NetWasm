using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class IntrinsicCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
            {
                continue;
            }
            var target = calledMethods.Resolve(call);
            if (target is null)
            {
                continue;
            }
            var replacement = (symbols.Format(target.Definition.DeclaringType),
                target.Definition.Name) switch
            {
                ("System.Type", "GetTypeFromHandle" or "GetTypeFromSemanticId") =>
                    CilOperation.MaterializeType,
                ("System.Type", "InternalGetFacts") when IsTypeFactsLookup(target) =>
                    CilOperation.GetTypeFacts,
                ("System.Reflection.MethodBase", "GetMethodFromHandle") =>
                    CilOperation.MaterializeMethod,
                ("System.Reflection.FieldInfo", "GetFieldFromHandle") =>
                    CilOperation.MaterializeField,
                ("System.Object", "GetType") => CilOperation.GetObjectType,
                _ => (CilOperation?)null,
            };
            if (replacement is null)
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = replacement.Value,
                Operand = replacement switch
                {
                    CilOperation.GetObjectType when
                        index > 0 &&
                        instructions[index - 1].Operation == CilOperation.Constrained &&
                        instructions[index - 1].Operand is CilOperand.TypeIdentity constrained =>
                        constrained,
                    CilOperation.MaterializeMethod or CilOperation.MaterializeField =>
                        new CilOperand.Index(target.Signature.ParameterTypes.Length),
                    _ => new CilOperand.None(),
                },
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private static bool IsTypeFactsLookup(MethodInstanceModel method) =>
        method.Definition.IsStatic &&
        method.Signature.ReturnSignatureType.StackKind == CliValueKind.NativeInt &&
        method.Signature.ParameterSignatureTypes is [{ StackKind: CliValueKind.I4 }];
}
