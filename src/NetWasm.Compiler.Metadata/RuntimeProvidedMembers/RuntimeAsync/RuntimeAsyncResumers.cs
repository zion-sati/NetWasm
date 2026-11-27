// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// A runtime-async frame resumes its method through a delegate over it (RuntimeAsyncFrame<TSlots, T1, ..., TResult>
// over Func<T1, ..., TResult>), which takes at most MaximumParameters parameters. A method with more resumes through a
// stub the compiler provides beside it (MethodDefinitionModel.RuntimeAsyncResumer): parameterless, static or instance
// as the method is, with its generic parameters and result, calling the method with placeholder arguments.
internal static class RuntimeAsyncResumers
{
    internal const int MaximumParameters = 16;

    // The stub's token: the method's with bit 30 set, which no metadata token has (a native marshalling stub's inner
    // method takes bit 31).
    internal static int Token(int methodToken) => methodToken | 0x40000000;

    // Whether a token is a stub's, and the token of the method it resumes: a stub has that method's generic parameters,
    // constraints included, which metadata records only for the method.
    internal static bool IsStub(int token) => (token & unchecked((int)0xC0000000)) == 0x40000000;

    internal static int Method(int stubToken) => stubToken & ~0x40000000;

    internal static bool Needs(MethodSignatureModel signature) =>
        signature.ParameterSignatureTypes.Length > MaximumParameters;

    // The stub of the method's instance, over the same declaring type and method arguments.
    internal static MethodInstanceModel For(IMethodRepository methods, MethodInstanceModel method)
    {
        var stub = methods.GetMethod(new EntityKey(method.Definition.Key.Assembly, Token(method.Definition.Key.MetadataToken)));
        return new(stub, method.DeclaringType, method.MethodArguments,
            stub.Signature.Substitute(method.DeclaringType.TypeArguments, method.MethodArguments));
    }
}
