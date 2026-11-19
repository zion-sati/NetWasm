// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

// A method whose IL names a member its declaring type lacks throws MissingMethodException or MissingFieldException
// when called, and one naming a type its assembly lacks throws TypeLoadException, as .NET throws one when it first
// compiles the method and Native AOT compiles the method to a throw.
internal sealed class MissingMemberBodyBuilder(
    ITypeFinderFactory types,
    MetadataCompilationSnapshot snapshot,
    IMethodRepository methods) : IMissingMemberBodyBuilder
{
    // The exceptions' (string message) constructors.
    private static readonly CliTypeIdentity[] MessageParameter =
        [CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false)];

    private readonly Lazy<MethodInstanceModel> _missingMethod = new(() => Constructor(types, snapshot, methods,
        "System.MissingMethodException"));

    private readonly Lazy<MethodInstanceModel> _missingField = new(() => Constructor(types, snapshot, methods,
        "System.MissingFieldException"));

    private readonly Lazy<MethodInstanceModel> _missingType = new(() => Constructor(types, snapshot, methods,
        "System.TypeLoadException"));

    public CilMethodBody Build(MethodInstanceModel method, MissingMemberReference missing)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(missing);
        var constructor = missing.Kind switch
        {
            MissingMemberKind.Method => _missingMethod.Value,
            MissingMemberKind.Field => _missingField.Value,
            _ => _missingType.Value,
        };
        return new(method.Definition, 1, [],
        [
            new(0, 1, CilOperation.LoadString, new CilOperand.UserString(missing.ExceptionMessage)),
            new(1, 2, CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            new(2, 3, CilOperation.Throw, new CilOperand.None()),
        ])
        {
            MethodInstance = method,
            MissingMember = missing,
        };
    }

    private static MethodInstanceModel Constructor(ITypeFinderFactory types, MetadataCompilationSnapshot snapshot,
        IMethodRepository methods, string exceptionType)
    {
        var exception = types.Create(snapshot).FindType(exceptionType);
        var constructor = exception.Methods.Select(methods.GetMethod)
            .Where(static method => method.Name == ".ctor")
            .Single(static method => method.Signature.ParameterSignatureTypes.SequenceEqual(MessageParameter));
        return new(constructor, CliTypeIdentity.FromDefinition(exception), [], constructor.Signature);
    }
}
