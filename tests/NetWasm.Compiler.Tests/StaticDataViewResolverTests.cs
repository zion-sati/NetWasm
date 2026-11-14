using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class StaticDataViewResolverTests
{
    [Fact]
    public void ExactPureProducerResolvesOnceAndCachesItsView()
    {
        var fixture = new Fixture();
        var resolver = fixture.Resolver();

        var first = resolver.Resolve(fixture.Producer);
        var second = resolver.Resolve(fixture.Producer);

        Assert.Same(fixture.View, first);
        Assert.Same(first, second);
        Assert.Equal(1, fixture.Bodies.ReadCount);
    }

    [Fact]
    public void StaticInitializerRejectsAnOtherwiseExactProducer()
    {
        var fixture = new Fixture(hasInitializer: true);

        var resolved = fixture.Resolver().Resolve(fixture.Producer);

        Assert.Null(resolved);
        Assert.Equal(0, fixture.Bodies.ReadCount);
    }

    [Fact]
    public void BeforeFieldInitProducerDoesNotTreatItsInitializerAsACallEffect()
    {
        var fixture = new Fixture(hasInitializer: true, beforeFieldInit: true);

        var resolved = fixture.Resolver().Resolve(fixture.Producer);

        Assert.Same(fixture.View, resolved);
        Assert.Equal(1, fixture.Bodies.ReadCount);
    }

    [Theory]
    [InlineData(ProducerFault.Instance)]
    [InlineData(ProducerFault.Parameter)]
    [InlineData(ProducerFault.OpenReturn)]
    [InlineData(ProducerFault.NoBody)]
    [InlineData(ProducerFault.WrongMethod)]
    [InlineData(ProducerFault.ZeroMaxStack)]
    [InlineData(ProducerFault.ExceptionRegion)]
    [InlineData(ProducerFault.ExtraOperation)]
    [InlineData(ProducerFault.WrongSpan)]
    [InlineData(ProducerFault.WrongElement)]
    [InlineData(ProducerFault.Undecodable)]
    public void AnyUnprovenProducerFactFallsBack(ProducerFault fault)
    {
        var fixture = new Fixture(fault: fault);

        var resolved = fixture.Resolver().Resolve(fixture.Producer);

        Assert.Null(resolved);
    }

    [Fact]
    public void NullProducerIsRejected()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentNullException>(() => fixture.Resolver().Resolve(null!));
    }

    public enum ProducerFault
    {
        None,
        Instance,
        Parameter,
        OpenReturn,
        NoBody,
        WrongMethod,
        ZeroMaxStack,
        ExceptionRegion,
        ExtraOperation,
        WrongSpan,
        WrongElement,
        Undecodable,
    }

    private sealed class Fixture
    {
        private readonly AssemblyIdentity _assembly = new("fixture");
        private readonly Repository _repository;

        public Fixture(
            bool hasInitializer = false,
            bool beforeFieldInit = false,
            ProducerFault fault = ProducerFault.None)
        {
            var ownerKey = Key(1);
            var producerKey = Key(2);
            var initializerKey = Key(3);
            Element = CliTypeIdentity.Primitive("u4", CliValueKind.I4);
            Span = CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(
                    _assembly,
                    "System",
                    "ReadOnlySpan`1",
                    isValueType: true),
                [Element]);
            var returnType = fault == ProducerFault.OpenReturn
                ? CliTypeIdentity.GenericInstantiation(
                    Span.ElementType!,
                    [CliTypeIdentity.GenericParameter(method: true, 0)])
                : Span;
            var definition = new MethodDefinitionModel(
                producerKey,
                ownerKey,
                "get_Data",
                fault != ProducerFault.Instance,
                MethodSignatureModel.Create(
                    returnType,
                    fault == ProducerFault.Parameter
                        ? [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]
                        : []),
                fault == ProducerFault.NoBody ? 0 : 1);
            var owner = CliTypeIdentity.Named(
                _assembly,
                "Fixture",
                "Owner",
                isValueType: false);
            Producer = new(definition, owner, [], definition.Signature);
            var dataType = CliTypeIdentity.Named(
                _assembly,
                "",
                "__StaticArrayInitTypeSize8",
                isValueType: true);
            var fieldDefinition = new FieldDefinitionModel(
                Key(4),
                Key(5),
                "Data",
                dataType,
                true)
            {
                InitialData = [1, 0, 0, 0, 2, 0, 0, 0],
            };
            var viewSpan = fault == ProducerFault.WrongSpan
                ? CliTypeIdentity.GenericInstantiation(
                    Span.ElementType!,
                    [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
                : Span;
            var viewElement = fault == ProducerFault.WrongElement
                ? CliTypeIdentity.Primitive("i4", CliValueKind.I4)
                : Element;
            View = new(
                new(fieldDefinition, owner, dataType),
                viewSpan,
                viewElement,
                2);
            var returnInstruction = new CilInstruction(
                1,
                2,
                CilOperation.Return,
                new CilOperand.None());
            var instructions = fault == ProducerFault.ExtraOperation
                ? ImmutableArray.Create(
                    new CilInstruction(0, 1, CilOperation.CreateReadOnlySpanFromRva, View),
                    new CilInstruction(1, 2, CilOperation.Duplicate, new CilOperand.None()),
                    new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()))
                : ImmutableArray.Create(
                    new CilInstruction(0, 1, CilOperation.CreateReadOnlySpanFromRva, View),
                    returnInstruction);
            var bodyMethod = fault == ProducerFault.WrongMethod
                ? definition with { Key = Key(99) }
                : definition;
            var body = new CilMethodBody(
                bodyMethod,
                fault == ProducerFault.ZeroMaxStack ? 0 : 1,
                [],
                instructions)
            {
                ExceptionRegions = fault == ProducerFault.ExceptionRegion
                    ? [new(
                        CilExceptionRegionKind.Finally,
                        0,
                        1,
                        1,
                        1,
                        null,
                        null)]
                    : [],
            };
            var initializer = new MethodDefinitionModel(
                initializerKey,
                ownerKey,
                ".cctor",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            var methods = hasInitializer
                ? ImmutableArray.Create(producerKey, initializerKey)
                : [producerKey];
            _repository = new(
                new TypeDefinitionModel(ownerKey, "Fixture", "Owner", false, [], methods)
                {
                    IsBeforeFieldInit = beforeFieldInit,
                },
                definition,
                initializer);
            Bodies = new(body, undecodable: fault == ProducerFault.Undecodable);
        }

        public CliTypeIdentity Element { get; }
        public CliTypeIdentity Span { get; }
        public MethodInstanceModel Producer { get; }
        public CilOperand.ReadOnlySpanData View { get; }
        public RecordingBodyReader Bodies { get; }

        public StaticDataViewResolver Resolver() => new(
            Bodies,
            new IdentitySpecializer(),
            _repository,
            _repository);

        private EntityKey Key(int token) => new(_assembly, token);
    }

    private sealed class RecordingBodyReader(CilMethodBody body, bool undecodable) : IMethodBodyReader, ICalleeMethodBodyReader
    {
        public bool TryReadMethodBody(
            MethodDefinitionModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public bool TryReadMethodBody(
            MethodInstanceModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public int ReadCount { get; private set; }

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) =>
            throw new InvalidOperationException();

        // An undecodable producer references something missing; its own analysis reports that.
        public CilMethodBody ReadMethodBody(MethodInstanceModel method)
        {
            ReadCount++;
            return undecodable ? throw new CompilerException(new(DiagnosticCode.MissingApi, "missing")) : body;
        }
    }

    private sealed class IdentitySpecializer : IMethodSpecializer
    {
        public CilMethodBody Rewrite(CilMethodBody body) => body;
    }

    private sealed class Repository(
        TypeDefinitionModel type,
        MethodDefinitionModel producer,
        MethodDefinitionModel initializer) : ITypeRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => type;

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            key == producer.Key ? producer : initializer;
    }
}
