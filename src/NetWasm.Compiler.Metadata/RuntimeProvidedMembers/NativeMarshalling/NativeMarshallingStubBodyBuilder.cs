// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// The body of a P/Invoke, or of an unmanaged calli's stub, that takes runtime marshalling, laid out as desktop .NET's
// IL stub is:
//   try { marshal each argument; prepare the result; call the native method; store the result; unmarshal }
//   finally { clean up each argument }
//   return the result
// The finally is there only when an argument needs cleanup. The native method is the import with the native signature
// the stub's declaration was given when its assembly was read, or for a calli's stub its last argument, the pointer,
// called with that signature. A call into a C variadic function loads its fixed
// arguments and the address of a buffer holding the rest (NativeVariadicArguments).
internal sealed class NativeMarshallingStubBodyBuilder(
    IMethodRepository methods,
    Func<INativeMarshallingClassifier> classifiers,
    ICilBodyLayout layout) : INativeMarshallingStubBodyBuilder
{
    private readonly Lazy<INativeMarshallingClassifier> _classifier = new(classifiers);

    public CilMethodBody Build(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var stub = method.Definition.NativeMarshallingStub ?? throw new ArgumentException(
            "The method is not a native marshalling stub.", nameof(method));
        // A function pointer's stub takes the pointer as its last argument, which no marshaller reads.
        var pointer = stub.FunctionPointer;
        var native = pointer is null ? methods.GetMethod(stub.NativeMethod) : null;
        var marshalled = pointer is null ? method : method with
        {
            Signature = new(method.Signature.ReturnSignatureType,
                [.. method.Signature.ParameterSignatureTypes.Take(method.Signature.ParameterSignatureTypes.Length - 1)]),
        };
        var streams = _classifier.Value.Classify(marshalled, pointer?.Declaration ?? native!.NativeImport!,
            pointer?.Native ?? native!.Signature);
        var arguments = streams.Arguments;
        var result = streams.Result;
        var cleanup = arguments.Any(argument => argument.NeedsCleanup);

        var tryStart = new CilBodyLabel();
        var handlerStart = new CilBodyLabel();
        var end = new CilBodyLabel();
        var locals = new List<CilBodyLocal>();
        var elements = new List<CilBodyElement> { Mark(tryStart) };
        elements.AddRange(arguments.SelectMany(argument => argument.Marshal));
        elements.AddRange(result.Prepare);
        if (stub.Variadic is { FixedParameters: var fixedParameters })
        {
            var nativeTypes = NativeMarshallingStubSignature.Lower(method.Signature) ?? method.Signature;
            var (buffer, store) = NativeVariadicArguments.Plan([.. arguments.Skip(fixedParameters)
                .Select((argument, index) => (argument.Load, nativeTypes.ParameterSignatureTypes[fixedParameters + index]))]);
            locals.Add(buffer);
            elements.AddRange(store);
            elements.AddRange(arguments.Take(fixedParameters).SelectMany(argument => argument.Load));
            elements.Add(Load(buffer));
        }
        else
        {
            elements.AddRange(result.Leading);
            elements.AddRange(arguments.SelectMany(argument => argument.Load));
        }
        if (pointer is null)
        {
            elements.Add(Call(new MethodInstanceModel(native!, method.DeclaringType, [], native!.Signature)));
        }
        else
        {
            elements.Add(Argument(method.Signature.ParameterSignatureTypes.Length - 1));
            elements.Add(Op(CilOperation.CallIndirect, new CilOperand.CallSite(pointer.Native, Unmanaged: true)));
        }
        elements.AddRange(result.Store);
        elements.AddRange(arguments.SelectMany(argument => argument.Unmarshal));
        if (cleanup)
        {
            elements.Add(Branch(CilOperation.Leave, end));
            elements.Add(Mark(handlerStart));
            elements.AddRange(arguments.SelectMany(argument => argument.Cleanup));
            elements.Add(Op(CilOperation.EndFinally));
        }
        elements.Add(Mark(end));
        elements.AddRange(result.Load);
        elements.Add(Op(CilOperation.Return));
        return layout.Lay(
            new(
                [.. arguments.SelectMany(argument => argument.Locals), .. result.Locals, .. locals],
                [.. elements],
                cleanup ? [new CilBodyFinally(tryStart, handlerStart, end)] : []),
            method,
            maxStack: Math.Max(method.Signature.ParameterSignatureTypes.Length, stub.Variadic is null ? 3 : 6) + 1);
    }
}
