// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

// A delegate's BeginInvoke and EndInvoke are runtime-provided. .NET Core implements
// both by throwing PlatformNotSupportedException, so NetWasm gives them that body.
internal sealed class DelegateAsynchronousCallBodyBuilder(
    ITypeFinderFactory types,
    MetadataCompilationSnapshot snapshot,
    IMethodRepository methods) : IDelegateAsynchronousCallBodyBuilder
{
    private const string Message = "Operation is not supported on this platform.";

    private readonly Lazy<MethodInstanceModel> _constructor = new(() =>
    {
        var exception = types.Create(snapshot).FindType("System.PlatformNotSupportedException");
        // The type declares only its three constructors; this one takes the message.
        var constructor = exception.Methods.Select(methods.GetMethod)
            .Single(static method =>
                method.Signature.ParameterSignatureTypes is [{ CanonicalName: "primitive:string" }]);
        return new(constructor, CliTypeIdentity.FromDefinition(exception), [], constructor.Signature);
    });

    public CilMethodBody Build(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return new(method.Definition, 1, [],
        [
            new(0, 1, CilOperation.LoadString, new CilOperand.UserString(Message)),
            new(1, 2, CilOperation.NewObject, new CilOperand.MethodInstance(_constructor.Value)),
            new(2, 3, CilOperation.Throw, new CilOperand.None()),
        ])
        {
            MethodInstance = method,
        };
    }
}
