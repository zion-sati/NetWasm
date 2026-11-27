// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IStateMachineBodyBuilder
{
    // The runtime async IL of a method Roslyn compiled to a state machine (MethodDefinitionModel.StateMachineMoveNext).
    CilMethodBody Build(MethodInstanceModel kickoff);
}

// Decodes the kickoff, then its machine's MoveNext for the machine the kickoff starts (its Start<TStateMachine>'s
// argument, so a generic method's machine is instantiated as the kickoff instantiates it), and rewrites them. A MoveNext
// that fails to decode (a member its assembly lacks) leaves the kickoff awaiting its task, so the machine compiles as
// any other method and reports what is missing.
internal sealed class StateMachineBodyBuilder(
    ICilDecoderFactory decoders,
    IManagedAssemblyResolver assemblies,
    IMethodRepository methods,
    IStateMachineRewriter rewriter) : IStateMachineBodyBuilder
{
    public CilMethodBody Build(MethodInstanceModel kickoff)
    {
        ArgumentNullException.ThrowIfNull(kickoff);
        var assembly = assemblies.Resolve(kickoff.Definition.Key.Assembly);
        var body = decoders.Create().Decode(assembly, kickoff);
        (MethodInstanceModel, CilMethodBody)? moveNext = null;
        if (kickoff.Definition.StateMachineMoveNext is { } key && Machine(body) is { } machine)
        {
            var definition = methods.GetMethod(key);
            var method = new MethodInstanceModel(definition, machine, [], definition.Signature);
            try
            {
                moveNext = (method, decoders.Create().Decode(assembly, method));
            }
            catch (CompilerException error) when (error.Diagnostic.MissingMember is not null)
            {
                moveNext = null;
            }
        }
        return rewriter.Rewrite(kickoff, body, moveNext);
    }

    // The machine the kickoff starts: the type argument of its builder's Start.
    private static CliTypeIdentity? Machine(CilMethodBody kickoff)
    {
        foreach (var instruction in kickoff.Instructions)
        {
            if (instruction is
                {
                    Operation: CilOperation.Call,
                    Operand: CilOperand.MethodInstance { Value: { Definition.Name: "Start", MethodArguments.Length: 1 } start },
                } && AsyncStateMachineKickoffs.TakesBuilder(start.DeclaringType))
            {
                return start.MethodArguments[0];
            }
        }
        return null;
    }
}
