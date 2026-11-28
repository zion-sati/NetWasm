// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeImports;

internal interface INativeImportForwarderBodyBuilder
{
    CilMethodBody Build(MethodInstanceModel forwarder);
}

// A native import's forwarder (NativeImportForwarders): its arguments, a call of the import, and the import's result.
// Compiled as any managed method, it takes and returns value types as the managed convention does, and its call of the
// import lowers to the native ABI as a direct call does.
internal sealed class NativeImportForwarderBodyBuilder(IMethodRepository methods) : INativeImportForwarderBodyBuilder
{
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public CilMethodBody Build(MethodInstanceModel forwarder)
    {
        ArgumentNullException.ThrowIfNull(forwarder);
        var import = _methods.GetMethod(forwarder.Definition.ForwardsNativeImport ??
            throw new ArgumentException("not a native import's forwarder", nameof(forwarder)));
        var parameters = forwarder.Signature.ParameterSignatureTypes;
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(parameters.Length + 2);
        void Add(CilOperation operation, CilOperand operand) =>
            instructions.Add(new(instructions.Count, instructions.Count + 1, operation, operand));
        for (var index = 0; index < parameters.Length; index++) Add(CilOperation.LoadArgument, new CilOperand.Index(index));
        Add(CilOperation.Call, new CilOperand.MethodInstance(new MethodInstanceModel(import, forwarder.DeclaringType, [],
            forwarder.Signature)));
        Add(CilOperation.Return, new CilOperand.None());
        return new(forwarder.Definition, Math.Max(parameters.Length, 1), [], instructions.MoveToImmutable())
        {
            MethodInstance = forwarder,
        };
    }
}

internal interface INativeImportAddressRedirector
{
    CilMethodBody Redirect(CilMethodBody body);
}

// The address of a native import with a forwarder (ldftn, for a delegate or a function pointer) is the forwarder's: the
// import's own function has the native ABI, which a delegate's invoke or a calli does not call.
internal sealed class NativeImportAddressRedirector(IMethodRepository methods) : INativeImportAddressRedirector
{
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public CilMethodBody Redirect(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? redirected = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (body.Instructions[index] is not
                {
                    Operation: CilOperation.LoadFunction,
                    Operand: CilOperand.MethodInstance { Value: { Definition.NativeImportForwarder: { } forwarder } import },
                } instruction)
            {
                continue;
            }
            redirected ??= body.Instructions.ToBuilder();
            redirected[index] = instruction with
            {
                Operand = new CilOperand.MethodInstance(new MethodInstanceModel(_methods.GetMethod(forwarder),
                    import.DeclaringType, [], import.Signature)),
            };
        }
        return redirected is null ? body : body with { Instructions = redirected.MoveToImmutable() };
    }
}
