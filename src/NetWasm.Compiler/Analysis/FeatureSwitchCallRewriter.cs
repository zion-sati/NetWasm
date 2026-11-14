using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Replaces a call to a feature switch's getter ([FeatureSwitchDefinition(name)] on its property) with the value the
// project gives the switch (a Trim="true" RuntimeHostConfigurationOption), and a call to a method an embedded
// ILLink.Substitutions.xml stubs under the project's switches with the stub's value, as ILLink substitutes them, so
// the constant-branch rules that follow remove the code the switch guards before reachability.
internal sealed class FeatureSwitchCallRewriter(
    ICalledMethodResolver calledMethods,
    ImmutableDictionary<string, bool> switches) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (switches.IsEmpty)
        {
            return body;
        }

        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { Definition.IsStatic: true } target ||
                !target.Signature.ParameterSignatureTypes.IsEmpty ||
                target.Signature.ReturnSignatureType.CanonicalName != "primitive:bool" ||
                Value(target.Definition) is not { } value)
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = CilOperation.LoadInt32,
                Operand = new CilOperand.ConstantI4(value ? 1 : 0),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private bool? Value(MethodDefinitionModel method) =>
        method.FeatureSwitch is { } name && switches.TryGetValue(name, out var value)
            ? value
            : method.FeatureSwitchStubs.Select(stub => stub.Select(switches)).FirstOrDefault(result => result is not null);
}
