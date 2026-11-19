// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeProvidedMembers.NativeMarshalling;

public sealed class NativeMarshallingStubBodyBuilderTests
{
    // Only a method the reader made a marshalling stub has a stub body; nothing else is consulted first.
    [Fact]
    public void RejectsAMethodThatIsNotAStub()
    {
        var assembly = new AssemblyIdentity("Stubs");
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001), "Plain", true,
            new(CliValueKind.Void, []), 0);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(assembly, "Stubs", "Holder", false), [],
            definition.Signature);
        var builder = new NativeMarshallingStubBodyBuilder(null!, () => throw new InvalidOperationException(), new CilBodyLayout());

        var exception = Assert.Throws<ArgumentException>(() => builder.Build(method));

        Assert.Equal("method", exception.ParamName);
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
    }

    // A call into a C variadic function loads its fixed arguments and then the address of a buffer holding the rest,
    // each stored as its native type: a string as the pointer its stub passes, an int as itself.
    [Theory]
    [InlineData(true, "nativeint")]
    [InlineData(false, "i4")]
    public void AVariadicCallPassesItsFixedArgumentsAndThenTheBufferOfTheRest(bool text, string stored)
    {
        var assembly = new AssemblyIdentity("Stubs");
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var rest = text ? CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false) : int32;
        var import = new NativeImportDeclaration("mule", "printf", default, IsVarArg: false, HasMarshalling: false, null);
        var native = new MethodDefinitionModel(new(assembly, 0x06000002), new(assembly, 0x02000001), "printf", true,
            new MethodSignatureModel(int32, [int32, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)]), 0)
        {
            NativeImport = import,
        };
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), [int32, rest]);
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001), "Print", true,
            signature, 0)
        {
            NativeMarshallingStub = new(native.Key) { Variadic = new(1) },
        };
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(assembly, "Stubs", "Holder", false), [], signature);
        var builder = new NativeMarshallingStubBodyBuilder(new Methods(native), () => new PassingClassifier(), new CilBodyLayout());

        var body = builder.Build(method);

        var instructions = body.Instructions;
        var call = instructions.Single(instruction => instruction.Operation == CilOperation.Call);
        var beforeCall = instructions[..instructions.IndexOf(call)];
        Assert.Contains(beforeCall, instruction => instruction.Operation == CilOperation.LocalAllocate);
        // The fixed argument, then the buffer.
        Assert.Equal([CilOperation.LoadArgument, CilOperation.LoadLocal], beforeCall[^2..].Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Index(0), beforeCall[^2].Operand);
        Assert.All(instructions.Where(instruction => instruction.Operation == CilOperation.StoreObject),
            instruction => Assert.Equal($"primitive:{stored}", ((CilOperand.TypeIdentity)instruction.Operand).Value.CanonicalName));
        Assert.Equal(7, body.MaxStack);
    }

    private sealed class Methods(MethodDefinitionModel native) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => native;
    }

    // Passes each argument as it is and returns nothing.
    private sealed class PassingClassifier : INativeMarshallingClassifier
    {
        public NativeStubStreams Classify(MethodInstanceModel stub, NativeImportDeclaration import, MethodSignatureModel native) =>
            new([.. stub.Signature.ParameterSignatureTypes.Select((_, index) =>
                    new NativeArgumentStreams([], [], [CilBodyElements.Argument(index)], [], []))],
                new NativeResultStreams([], [], [], []));
    }
}
