// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeProvidedMembers.NativeMarshalling;

public sealed class NativeMarshallingClassifierTests
{
    private static readonly AssemblyIdentity Assembly = new("Native");

    // Only malformed metadata has a struct that contains itself; its array is refused as not blittable rather than
    // walked forever. The field's type carries a modifier, as one the reader keeps (a calling convention) would.
    [Fact]
    public void AStructThatContainsItselfIsNotBlittable()
    {
        var cyclic = CliTypeIdentity.Named(Assembly, "Native", "Cyclic", isValueType: true);
        var fieldKey = new EntityKey(Assembly, 0x04000001);
        var definition = new TypeDefinitionModel(new(Assembly, 0x02000002), "Native", "Cyclic", true, [fieldKey], [])
        {
            LayoutKind = CliTypeLayoutKind.Sequential,
        };
        var modifier = CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "CallConvCdecl", false);
        var field = new FieldDefinitionModel(fieldKey, definition.Key, "Self", CliValueKind.ValueType, false)
        {
            SignatureType = CliTypeIdentity.Modified(cyclic, modifier, false),
        };
        var classifier = new NativeMarshallingClassifier(new Definitions(definition), null!, null!, new Fields(field), null!,
            new NativeStructCopierFactory(new CompilationNativeStructSource(new Definitions(definition), new Fields(field))));
        var stubDefinition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Fill", true,
            new(CliValueKind.Void, [CliValueKind.ManagedReference]), 0);
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), [CliTypeIdentity.SzArray(cyclic)]);
        var stub = new MethodInstanceModel(stubDefinition, CliTypeIdentity.Named(Assembly, "Native", "Holder", false), [], signature);

        var import = new NativeImportDeclaration("mule", "fill", default, IsVarArg: false, HasMarshalling: false, null);
        var exception = Assert.ThrowsAny<Exception>(() => classifier.Classify(stub, import, signature));

        Assert.Contains("must have blittable elements", exception.Message, StringComparison.Ordinal);
    }

    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Bool = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Native", "Pair", isValueType: true);
    private static readonly NativeImportDeclaration Import = new("mule", "f", default, IsVarArg: false, HasMarshalling: false, null);

    // A struct result the native function writes through a leading pointer: the stub passes a zeroed native copy's
    // address first and converts the copy into the result after the call.
    [Fact]
    public void AStructResultThroughTheLeadingPointerConvertsFromAZeroedNativeCopy()
    {
        var copier = new RecordingCopier();
        var signature = new MethodSignatureModel(Pair, [Int32]);

        var streams = Classifier(copier).Classify(Stub(signature), Import, new MethodSignatureModel(Void, [NativeInt, Int32]));

        Assert.Equal([CilOperation.LoadLocal], streams.Result.Leading.Select(element =>
            ((CilBodyElement.LocalInstruction)element).Operation));
        Assert.Equal([NativeCopyDirection.ToManaged], copier.Directions);
        Assert.Equal([CilOperation.LoadArgument], streams.Arguments.Single().Load.Select(element =>
            ((CilBodyElement.Instruction)element).Operation));
    }

    // A struct by reference copies in only with [In], back only with [Out], and both ways with neither or both.
    [Theory]
    [InlineData(NativeParameterFlow.None, true, true)]
    [InlineData(NativeParameterFlow.In, true, false)]
    [InlineData(NativeParameterFlow.Out, false, true)]
    [InlineData(NativeParameterFlow.In | NativeParameterFlow.Out, true, true)]
    public void AStructByReferenceCopiesTheWaysItsFlowSays(NativeParameterFlow flow, bool copiesIn, bool copiesOut)
    {
        var copier = new RecordingCopier();
        var signature = new MethodSignatureModel(Void, [CliTypeIdentity.ManagedByReference(Pair)]);

        Classifier(copier).Classify(Stub(signature), Import with { ParameterFlows = new([flow]) },
            new MethodSignatureModel(Void, [NativeInt]));

        Assert.Equal((copiesIn, copiesOut), (copier.Directions.Contains(NativeCopyDirection.ToNative),
            copier.Directions.Contains(NativeCopyDirection.ToManaged)));
    }

    // A variadic call's native method takes the fixed parameters and then the buffer, so each argument keeps its own type,
    // a struct included.
    [Fact]
    public void AVariadicCallsArgumentsKeepTheirOwnTypes()
    {
        var copier = new RecordingCopier();
        var signature = new MethodSignatureModel(Void, [Int32, Pair]);

        var streams = Classifier(copier).Classify(Stub(signature, variadic: true), Import,
            new MethodSignatureModel(Void, [Int32, NativeInt]));

        Assert.Equal(2, streams.Arguments.Length);
        Assert.All(streams.Arguments, argument => Assert.Empty(argument.Marshal));
        Assert.Empty(copier.Directions);
    }

    // A result desktop .NET's runtime marshalling refuses, or NetWasm's does not provide, fails the build, naming why.
    [Theory]
    [InlineData(false, "Invalid managed/unmanaged type combination.")]
    [InlineData(true, "must be a string, a SafeHandle or a CriticalHandle")]
    public void ResultsMarshallingCannotReturnFail(bool aClass, string reason)
    {
        var result = aClass ? CliTypeIdentity.Named(Assembly, "Native", "Plain", false) : Int32;
        var import = Import with { MarshalAs = new(aClass ? null : UnmanagedType.I8, []) };

        var exception = Assert.ThrowsAny<Exception>(() => Classifier(null).Classify(
            Stub(new MethodSignatureModel(result, [])), import, new MethodSignatureModel(aClass ? NativeInt : Int32, [])));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    // What desktop .NET's runtime marshalling refuses, or NetWasm's does not provide, fails the build, naming why.
    [Theory]
    [InlineData("struct", "must lay out natively")]
    [InlineData("widened", "Invalid managed/unmanaged type combination.")]
    [InlineData("variant", "booleans must be paired")]
    [InlineData("class by reference", "must be a SafeHandle or CriticalHandle")]
    public void PositionsMarshallingCannotPassFail(string position, string reason)
    {
        var (parameter, marshalAs, native) = position switch
        {
            "struct" => (Pair, (UnmanagedType?)null, NativeInt),
            "widened" => (Int32, UnmanagedType.I8, Int32),
            "variant" => (Bool, UnmanagedType.VariantBool, Int32),
            _ => (CliTypeIdentity.ManagedByReference(CliTypeIdentity.Named(Assembly, "Native", "Plain", false)), null,
                NativeInt),
        };
        var import = Import with { MarshalAs = new(null, [marshalAs]) };

        var exception = Assert.ThrowsAny<Exception>(() => Classifier(null).Classify(
            Stub(new MethodSignatureModel(Void, [parameter])), import, new MethodSignatureModel(Void, [native])));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    private static NativeMarshallingClassifier Classifier(INativeStructCopier? copier) =>
        new(null!, new NoBases(), null!, null!, null!, new Copiers(copier));

    private static MethodInstanceModel Stub(MethodSignatureModel signature, bool variadic = false)
    {
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Call", true,
            new(signature.ReturnSignatureType.StackKind, [.. signature.ParameterSignatureTypes.Select(type => type.StackKind)]), 0)
        {
            NativeMarshallingStub = new(new EntityKey(Assembly, 0x06000002)) { Variadic = variadic ? new(1) : null },
        };
        return new(definition, CliTypeIdentity.Named(Assembly, "Native", "Holder", false), [], signature);
    }

    private sealed class RecordingCopier : INativeStructCopier
    {
        public List<NativeCopyDirection> Directions { get; } = [];

        public int NativeSize => 16;

        public ImmutableArray<CilBodyElement> Copy(ImmutableArray<CilBodyElement> managed, CilBodyLocal buffer,
            NativeCopyDirection direction)
        {
            Directions.Add(direction);
            return [];
        }
    }

    private sealed class Copiers(INativeStructCopier? copier) : INativeStructCopierFactory
    {
        public INativeStructCopier? Create(CliTypeIdentity type, NativeScalarConversions conversions) => copier;
    }

    private sealed class NoBases : IBaseTypeIdentityResolver
    {
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type) => null;
    }

    private sealed class Definitions(TypeDefinitionModel definition) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => definition;
    }

    private sealed class Fields(FieldDefinitionModel field) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => field;
    }
}
