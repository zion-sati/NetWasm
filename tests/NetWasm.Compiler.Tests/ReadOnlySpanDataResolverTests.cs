// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ReadOnlySpanDataResolverTests
{
    [Fact]
    public void ExactRuntimeHelperAndRvaFieldProduceImmutableProvenance()
    {
        var fixture = new Fixture();

        var resolved = new ReadOnlySpanDataResolver().Resolve(
            fixture.Target,
            fixture.Field);

        Assert.NotNull(resolved);
        Assert.Equal(fixture.Field, resolved.Field);
        Assert.Equal(fixture.Span, resolved.SpanType);
        Assert.Equal(fixture.Element, resolved.ElementType);
        Assert.Equal(2, resolved.ElementCount);
    }

    [Theory]
    [InlineData("bool", CliValueKind.I4, 1)]
    [InlineData("i1", CliValueKind.I4, 1)]
    [InlineData("u1", CliValueKind.I4, 1)]
    [InlineData("char", CliValueKind.I4, 2)]
    [InlineData("i2", CliValueKind.I4, 2)]
    [InlineData("u2", CliValueKind.I4, 2)]
    [InlineData("i4", CliValueKind.I4, 4)]
    [InlineData("u4", CliValueKind.I4, 4)]
    [InlineData("f4", CliValueKind.F4, 4)]
    [InlineData("i8", CliValueKind.I8, 8)]
    [InlineData("u8", CliValueKind.I8, 8)]
    [InlineData("f8", CliValueKind.F8, 8)]
    public void EverySupportedPrimitiveWidthProducesTwoElements(
        string canonicalName,
        CliValueKind stackKind,
        int elementSize)
    {
        var fixture = new Fixture(
            elementCanonicalName: canonicalName,
            elementStackKind: stackKind,
            elementSize: elementSize);

        var resolved = new ReadOnlySpanDataResolver().Resolve(
            fixture.Target,
            fixture.Field);

        Assert.NotNull(resolved);
        Assert.Equal($"primitive:{canonicalName}", resolved.ElementType.CanonicalName);
        Assert.Equal(2, resolved.ElementCount);
        Assert.Equal(elementSize * 2, resolved.Field.Definition.InitialData.Length);
    }

    // Roslyn reads a constant span of an enum from data as its underlying type's (RuntimeHelpers.CreateSpan<TEnum>).
    [Theory]
    [InlineData("u1", CliValueKind.I4, 1)]
    [InlineData("i2", CliValueKind.I4, 2)]
    [InlineData("u4", CliValueKind.I4, 4)]
    [InlineData("i8", CliValueKind.I8, 8)]
    public void AnEnumElementTakesItsUnderlyingPrimitivesSize(string underlying, CliValueKind stackKind, int elementSize)
    {
        var element = CliTypeIdentity.Named(new AssemblyIdentity("App"), "App", "Color", isValueType: true, stackKind)
            .WithStackStorageType(CliTypeIdentity.Primitive(underlying, stackKind));
        var fixture = new Fixture(elementSize: elementSize, element: element);

        var resolved = new ReadOnlySpanDataResolver().Resolve(fixture.Target, fixture.Field);

        Assert.NotNull(resolved);
        Assert.Equal(element, resolved.ElementType);
        Assert.Equal(2, resolved.ElementCount);
        Assert.True(ReadOnlySpanDataResolver.TryGetElementSize(element, out var size));
        Assert.Equal(elementSize, size);
    }

    [Fact]
    public void AStructElementWithoutAPrimitiveStorageIsNotConstantData()
    {
        var element = CliTypeIdentity.Named(new AssemblyIdentity("App"), "App", "Pair", isValueType: true);

        var fixture = new Fixture(element: element);

        Assert.False(ReadOnlySpanDataResolver.TryGetElementSize(element, out _));
        Assert.Null(new ReadOnlySpanDataResolver().Resolve(fixture.Target, fixture.Field));
    }

    [Theory]
    [InlineData(ResolverFault.NullTarget)]
    [InlineData(ResolverFault.DeclaringAssembly)]
    [InlineData(ResolverFault.DeclaringName)]
    [InlineData(ResolverFault.MethodName)]
    [InlineData(ResolverFault.Instance)]
    [InlineData(ResolverFault.NonPublic)]
    [InlineData(ResolverFault.Arity)]
    [InlineData(ResolverFault.MethodArguments)]
    [InlineData(ResolverFault.MultipleMethodArguments)]
    [InlineData(ResolverFault.OpenElement)]
    [InlineData(ResolverFault.ParameterCount)]
    [InlineData(ResolverFault.MultipleParameters)]
    [InlineData(ResolverFault.HandleAssembly)]
    [InlineData(ResolverFault.HandleName)]
    [InlineData(ResolverFault.ReturnShape)]
    [InlineData(ResolverFault.ReturnName)]
    [InlineData(ResolverFault.ReturnAssembly)]
    [InlineData(ResolverFault.ReturnElement)]
    [InlineData(ResolverFault.ReturnArguments)]
    [InlineData(ResolverFault.MultipleReturnArguments)]
    [InlineData(ResolverFault.UnsupportedElement)]
    [InlineData(ResolverFault.InstanceField)]
    [InlineData(ResolverFault.DefaultData)]
    [InlineData(ResolverFault.EmptyData)]
    [InlineData(ResolverFault.PartialElement)]
    public void AnyContractMismatchFallsBack(ResolverFault fault)
    {
        var fixture = new Fixture(fault);

        var resolved = new ReadOnlySpanDataResolver().Resolve(
            fault == ResolverFault.NullTarget ? null : fixture.Target,
            fixture.Field);

        Assert.Null(resolved);
    }

    public enum ResolverFault
    {
        None,
        NullTarget,
        DeclaringAssembly,
        DeclaringName,
        MethodName,
        Instance,
        NonPublic,
        Arity,
        MethodArguments,
        MultipleMethodArguments,
        OpenElement,
        ParameterCount,
        MultipleParameters,
        HandleAssembly,
        HandleName,
        ReturnShape,
        ReturnName,
        ReturnAssembly,
        ReturnElement,
        ReturnArguments,
        MultipleReturnArguments,
        UnsupportedElement,
        InstanceField,
        DefaultData,
        EmptyData,
        PartialElement,
    }

    private sealed class Fixture
    {
        public Fixture(
            ResolverFault fault = ResolverFault.None,
            string elementCanonicalName = "u1",
            CliValueKind elementStackKind = CliValueKind.I4,
            int elementSize = 1,
            CliTypeIdentity? element = null)
        {
            var core = new AssemblyIdentity("NetWasm.CoreLib");
            var other = new AssemblyIdentity("Other");
            Element = element ?? (fault == ResolverFault.OpenElement
                ? CliTypeIdentity.GenericParameter(method: true, 0)
                : fault == ResolverFault.UnsupportedElement
                    ? CliTypeIdentity.Primitive(
                        "nativeint",
                        CliValueKind.NativeInt)
                    : fault == ResolverFault.PartialElement
                        ? CliTypeIdentity.Primitive("u4", CliValueKind.I4)
                    : CliTypeIdentity.Primitive(
                        elementCanonicalName,
                        elementStackKind));
            var spanDefinition = CliTypeIdentity.Named(
                fault == ResolverFault.ReturnAssembly ? other : core,
                "System",
                fault == ResolverFault.ReturnName ? "OtherSpan`1" : "ReadOnlySpan`1",
                isValueType: true);
            Span = fault == ResolverFault.ReturnShape
                ? CliTypeIdentity.Primitive("i4", CliValueKind.I4)
                : CliTypeIdentity.GenericInstantiation(
                    spanDefinition,
                    fault switch
                    {
                        ResolverFault.ReturnArguments => [],
                        ResolverFault.MultipleReturnArguments => [Element, Element],
                        _ => [fault == ResolverFault.ReturnElement
                            ? CliTypeIdentity.Primitive("i4", CliValueKind.I4)
                            : Element],
                    });
            var handle = CliTypeIdentity.Named(
                fault == ResolverFault.HandleAssembly ? other : core,
                "System",
                fault == ResolverFault.HandleName
                    ? "OtherHandle"
                    : "RuntimeFieldHandle",
                isValueType: true);
            var runtimeHelpers = CliTypeIdentity.Named(
                fault == ResolverFault.DeclaringAssembly ? other : core,
                "System.Runtime.CompilerServices",
                fault == ResolverFault.DeclaringName
                    ? "OtherHelpers"
                    : "RuntimeHelpers",
                isValueType: false);
            var definition = new MethodDefinitionModel(
                new(core, 1),
                new(core, 2),
                fault == ResolverFault.MethodName ? "Other" : "CreateSpan",
                fault != ResolverFault.Instance,
                MethodSignatureModel.Create(
                    Span,
                    fault switch
                    {
                        ResolverFault.ParameterCount => [],
                        ResolverFault.MultipleParameters => [handle, handle],
                        _ => [handle],
                    }),
                0)
            {
                GenericArity = fault == ResolverFault.Arity ? 0 : 1,
                IsPublic = fault != ResolverFault.NonPublic,
            };
            Target = new(
                definition,
                runtimeHelpers,
                fault switch
                {
                    ResolverFault.MethodArguments => [],
                    ResolverFault.MultipleMethodArguments => [Element, Element],
                    _ => [Element],
                },
                definition.Signature);
            var owner = CliTypeIdentity.Named(
                core,
                "",
                "<PrivateImplementationDetails>",
                isValueType: false);
            var dataType = CliTypeIdentity.Named(
                core,
                "",
                "__StaticArrayInitTypeSize2",
                isValueType: true);
            byte[] bytes = fault switch
            {
                ResolverFault.EmptyData => [],
                ResolverFault.PartialElement => [1, 2],
                _ => new byte[checked(elementSize * 2)],
            };
            var field = new FieldDefinitionModel(
                new(core, 3),
                new(core, 4),
                "Data",
                dataType,
                fault != ResolverFault.InstanceField);
            if (fault != ResolverFault.DefaultData)
            {
                field = field with { InitialData = [.. bytes] };
            }
            Field = new(field, owner, dataType);
        }

        public CliTypeIdentity Element { get; }
        public CliTypeIdentity Span { get; }
        public MethodInstanceModel Target { get; }
        public FieldInstanceModel Field { get; }
    }
}
