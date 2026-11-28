// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// A runtime-async method's resumption stub (RuntimeAsyncResumers): this, a zeroed value of each parameter's type, and a
// call of the method, which finds itself resumed and takes its arguments back from its frame. The call is not virtual:
// the frame belongs to this method's activation.
internal sealed class RuntimeAsyncResumerBodyBuilder(IMethodRepository methods) : IRuntimeAsyncResumerBodyBuilder
{
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public CilMethodBody Build(MethodInstanceModel resumer)
    {
        ArgumentNullException.ThrowIfNull(resumer);
        var definition = _methods.GetMethod(resumer.Definition.RuntimeAsyncResumer ??
            throw new ArgumentException("not a runtime-async resumption stub", nameof(resumer)));
        var method = new MethodInstanceModel(definition, resumer.DeclaringType, resumer.MethodArguments,
            definition.Signature.Substitute(resumer.DeclaringType.TypeArguments, resumer.MethodArguments));
        var parameters = method.Signature.ParameterSignatureTypes;
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
        void Add(CilOperation operation, CilOperand operand) =>
            instructions.Add(new(instructions.Count, instructions.Count + 1, operation, operand));
        if (!definition.IsStatic) Add(CilOperation.LoadArgument, new CilOperand.Index(0));
        for (var index = 0; index < parameters.Length; index++)
        {
            Add(CilOperation.LoadLocalAddress, new CilOperand.Index(index));
            Add(CilOperation.InitializeObject, new CilOperand.TypeIdentity(parameters[index]));
            Add(CilOperation.LoadLocal, new CilOperand.Index(index));
        }
        Add(CilOperation.Call, new CilOperand.MethodInstance(method));
        Add(CilOperation.Return, new CilOperand.None());
        return new(resumer.Definition, parameters.Length + 2, [.. parameters.Select(type => type.StackKind)],
            instructions.ToImmutable())
        {
            LocalSignatureTypes = parameters,
            MethodInstance = resumer,
        };
    }
}
