using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Assembly.GetExecutingAssembly() is the assembly of the method that calls it, which compiling knows,
// so the call becomes RuntimeAssembly.Of<TDeclaring>() for the calling method's declaring type.
internal sealed class ExecutingAssemblyCallRewriter(
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    ITypeIdentityResolver typeIdentities) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToBuilder();
        var changed = false;
        for (var index = 0; index < instructions.Count; index++)
        {
            if (ExecutingAssembly(instructions[index]) is not { } target)
            {
                continue;
            }
            instructions[index] = instructions[index] with
            {
                Operation = CilOperation.Call,
                Operand = new CilOperand.MethodInstance(Of(target, body)),
            };
            changed = true;
        }
        return changed ? body with { Instructions = instructions.ToImmutable() } : body;
    }

    private MethodInstanceModel? ExecutingAssembly(CilInstruction instruction) =>
        instruction.Operation == CilOperation.Call &&
        calledMethods.Resolve(instruction) is { Definition: { Name: "GetExecutingAssembly", IsStatic: true } } target &&
        target.Signature.ParameterTypes.IsEmpty &&
        symbols.Format(target.Definition.DeclaringType) == "System.Reflection.Assembly"
            ? target
            : null;

    private MethodInstanceModel Of(MethodInstanceModel getExecutingAssembly, CilMethodBody body)
    {
        var assembly = getExecutingAssembly.Definition.Key.Assembly;
        var of = typeDefinitions
            .ResolveTypeIdentity(CliTypeIdentity.Named(assembly, "System.Reflection", "RuntimeAssembly", false))
            .Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == "Of");
        var declaringType = body.MethodInstance?.DeclaringType ?? typeIdentities.GetTypeIdentity(body.Method.DeclaringType);
        return methodInstances.ResolveMethodInstance(
            assembly,
            of.Key.MetadataToken,
            symbols.Format(of),
            0,
            new CliGenericContext([], [declaringType]));
    }
}
