using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.LoopInvariantFields;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.LoopInvariantFields;

public sealed class FieldProjectionResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("FieldProjection.Tests");
    private static readonly CliTypeIdentity Aggregate = CliTypeIdentity.Named(
        Assembly,
        "Fixtures",
        "Aggregate",
        isValueType: true);
    private static readonly CliTypeIdentity OtherAggregate = CliTypeIdentity.Named(
        Assembly,
        "Fixtures",
        "OtherAggregate",
        isValueType: true);
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Reference = CliTypeIdentity.SzArray(Int32);

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.ManagedReference)]
    public void ResolveAcceptsDirectSupportedFieldReads(CliValueKind fieldKind)
    {
        var fieldType = fieldKind == CliValueKind.ManagedReference ? Reference : Int32;
        var field = Field(fieldType);
        var instruction = Instruction(
            CilOperation.LoadField,
            new CilOperand.FieldInstance(field));

        var projection = Create().Resolve(Aggregate, instruction);

        Assert.NotNull(projection);
        Assert.Same(instruction, projection.Instruction);
        Assert.Same(field, projection.Field);
    }

    [Fact]
    public void ResolveNormalizesDefinitionFieldOperands()
    {
        var field = Field(Reference);
        var instruction = Instruction(
            CilOperation.LoadField,
            new CilOperand.Entity(field.Definition.Key));

        var projection = Create(field: field).Resolve(Aggregate, instruction);

        Assert.NotNull(projection);
        Assert.Same(field.Definition, projection.Field.Definition);
        Assert.Equal(Aggregate, projection.Field.DeclaringType);
        Assert.Equal(Reference, projection.Field.FieldType);
    }

    [Fact]
    public void ResolveRejectsDefinitionFieldFromAnotherAggregate()
    {
        var field = Field(Reference) with
        {
            Definition = Field(Reference).Definition with
            {
                DeclaringType = new EntityKey(Assembly, 0x02000002),
            },
        };
        var instruction = Instruction(
            CilOperation.LoadField,
            new CilOperand.Entity(field.Definition.Key));

        Assert.Null(Create(field: field).Resolve(Aggregate, instruction));
    }

    [Fact]
    public void ResolveRejectsDefinitionFieldForConstructedAggregateWithoutLookup()
    {
        var getter = Getter(Int32);
        var resolver = new FieldProjectionResolver(
            new FixedMethodBodyReader(GetterBody(getter)),
            new FixedCalledMethodResolver(getter),
            new ThrowingFieldRepository(),
            new FixedTypeDefinitionResolver(CliTypeLayoutKind.Sequential));
        var constructed = CliTypeIdentity.GenericInstantiation(Aggregate, [Int32]);

        Assert.Null(resolver.Resolve(
            constructed,
            Instruction(
                CilOperation.LoadField,
                new CilOperand.Entity(Field(Int32).Definition.Key))));
    }

    [Fact]
    public void ResolveRejectsUnsupportedFieldOperand()
    {
        Assert.Null(Create().Resolve(
            Aggregate,
            Instruction(CilOperation.LoadField, new CilOperand.None())));
    }

    [Fact]
    public void ResolveAcceptsOnlyExactSingleFieldGetterBehavior()
    {
        var field = Field(Reference);
        var getter = Getter(Reference);
        var body = GetterBody(
            getter,
            new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new CilInstruction(2, 3, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
            new CilInstruction(3, 4, CilOperation.Return, new CilOperand.None()));
        var call = Instruction(
            CilOperation.Call,
            new CilOperand.MethodInstance(getter));

        var projection = Create(getter, body).Resolve(Aggregate, call);

        Assert.NotNull(projection);
        Assert.Same(call, projection.Instruction);
        Assert.Same(field, projection.Field);
    }

    [Fact]
    public void ResolveAcceptsDefinitionFieldOperandInsideExactGetter()
    {
        var field = Field(Reference);
        var getter = Getter(Reference);
        var body = GetterBody(
            getter,
            new CilInstruction(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new CilInstruction(
                1,
                2,
                CilOperation.LoadField,
                new CilOperand.Entity(field.Definition.Key)),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()));
        var call = Instruction(
            CilOperation.Call,
            new CilOperand.MethodInstance(getter));

        var projection = Create(getter, body, field: field).Resolve(Aggregate, call);

        Assert.NotNull(projection);
        Assert.Same(field.Definition, projection.Field.Definition);
    }

    [Theory]
    [InlineData(CilOperation.CallVirtual)]
    [InlineData(CilOperation.Pop)]
    public void ResolveRejectsUnsupportedProjectionOperations(CilOperation operation)
    {
        var getter = Getter(Int32);
        var instruction = Instruction(
            operation,
            operation == CilOperation.CallVirtual
                ? new CilOperand.MethodInstance(getter)
                : new CilOperand.None());

        Assert.Null(Create(getter).Resolve(Aggregate, instruction));
    }

    [Fact]
    public void ResolveRejectsExplicitLayoutAggregates()
    {
        var resolver = Create(
            layout: CliTypeLayoutKind.Explicit);

        Assert.Null(resolver.Resolve(
            Aggregate,
            Instruction(
                CilOperation.LoadField,
                new CilOperand.FieldInstance(Field(Int32)))));
    }

    [Fact]
    public void ResolveRejectsStaticOverlappingMismatchedAndValueFields()
    {
        Assert.Null(Create().Resolve(Aggregate, FieldInstruction(
            Field(Int32) with
            {
                Definition = Field(Int32).Definition with { IsStatic = true },
            })));
        Assert.Null(Create().Resolve(Aggregate, FieldInstruction(
            Field(Int32) with
            {
                Definition = Field(Int32).Definition with { ExplicitOffset = 0 },
            })));
        Assert.Null(Create().Resolve(Aggregate, FieldInstruction(
            Field(Int32) with { DeclaringType = OtherAggregate })));
        Assert.Null(Create().Resolve(Aggregate, FieldInstruction(
            Field(Aggregate))));
    }

    [Theory]
    [InlineData(GetterShape.Unresolved)]
    [InlineData(GetterShape.Static)]
    [InlineData(GetterShape.Virtual)]
    [InlineData(GetterShape.Abstract)]
    [InlineData(GetterShape.NoBody)]
    [InlineData(GetterShape.WrongDeclaringType)]
    [InlineData(GetterShape.Parameterized)]
    [InlineData(GetterShape.ValueReturn)]
    [InlineData(GetterShape.ExceptionRegion)]
    [InlineData(GetterShape.WrongBody)]
    [InlineData(GetterShape.WrongReturnType)]
    public void ResolveRejectsGetterWithoutExactFieldProjection(GetterShape shape)
    {
        var field = Field(Int32);
        var getter = Getter(Int32);
        getter = shape switch
        {
            GetterShape.Static => getter with
            {
                Definition = getter.Definition with { IsStatic = true },
            },
            GetterShape.Virtual => getter with
            {
                Definition = getter.Definition with { IsVirtual = true },
            },
            GetterShape.Abstract => getter with
            {
                Definition = getter.Definition with { IsAbstract = true },
            },
            GetterShape.NoBody => getter with
            {
                Definition = getter.Definition with { RelativeVirtualAddress = 0 },
            },
            GetterShape.WrongDeclaringType => getter with
            {
                DeclaringType = OtherAggregate,
            },
            GetterShape.Parameterized => getter with
            {
                Signature = MethodSignatureModel.Create(Int32, Int32),
            },
            GetterShape.ValueReturn => Getter(Aggregate),
            _ => getter,
        };
        var bodyField = shape == GetterShape.WrongReturnType
            ? Field(Reference)
            : field;
        var body = GetterBody(
            getter,
            new CilInstruction(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new CilInstruction(
                1,
                2,
                shape == GetterShape.WrongBody
                    ? CilOperation.LoadFieldAddress
                    : CilOperation.LoadField,
                new CilOperand.FieldInstance(bodyField)),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()));
        if (shape == GetterShape.ExceptionRegion)
        {
            body = body with
            {
                ExceptionRegions =
                [
                    new CilExceptionRegion(
                        CilExceptionRegionKind.Finally,
                        0,
                        1,
                        1,
                        2,
                        null,
                        null),
                ],
            };
        }
        var resolver = Create(
            shape == GetterShape.Unresolved ? null : getter,
            body);

        Assert.Null(resolver.Resolve(
            Aggregate,
            Instruction(
                CilOperation.Call,
                new CilOperand.MethodInstance(getter))));
    }

    [Fact]
    public void ResolveRejectsMissingArguments()
    {
        var resolver = Create();
        var instruction = FieldInstruction(Field(Int32));

        Assert.Throws<ArgumentNullException>(() =>
            resolver.Resolve(null!, instruction));
        Assert.Throws<ArgumentNullException>(() =>
            resolver.Resolve(Aggregate, null!));
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        var getter = Getter(Int32);
        var body = GetterBody(getter);
        var bodies = new FixedMethodBodyReader(body);
        var calls = new FixedCalledMethodResolver(getter);
        var fields = new FixedFieldRepository(Field(Int32).Definition);
        var types = new FixedTypeDefinitionResolver(CliTypeLayoutKind.Sequential);

        Assert.Throws<ArgumentNullException>(() =>
            new FieldProjectionResolver(null!, calls, fields, types));
        Assert.Throws<ArgumentNullException>(() =>
            new FieldProjectionResolver(bodies, null!, fields, types));
        Assert.Throws<ArgumentNullException>(() =>
            new FieldProjectionResolver(bodies, calls, null!, types));
        Assert.Throws<ArgumentNullException>(() =>
            new FieldProjectionResolver(bodies, calls, fields, null!));
    }

    private static FieldProjectionResolver Create(
        MethodInstanceModel? getter = null,
        CilMethodBody? body = null,
        CliTypeLayoutKind layout = CliTypeLayoutKind.Sequential,
        FieldInstanceModel? field = null)
    {
        field ??= Field(Int32);
        return new(
            new FixedMethodBodyReader(body ?? GetterBody(getter ?? Getter(Int32))),
            new FixedCalledMethodResolver(getter),
            new FixedFieldRepository(field.Definition),
            new FixedTypeDefinitionResolver(layout));
    }

    private static CilInstruction FieldInstruction(FieldInstanceModel field) =>
        Instruction(CilOperation.LoadField, new CilOperand.FieldInstance(field));

    private static CilInstruction Instruction(
        CilOperation operation,
        CilOperand operand) => new(10, 11, operation, operand);

    private static FieldInstanceModel Field(CliTypeIdentity type)
    {
        var definition = new FieldDefinitionModel(
            new(Assembly, 0x04000001),
            new(Assembly, 0x02000001),
            "Value",
            type,
            isStatic: false);
        return new(definition, Aggregate, type);
    }

    private static MethodInstanceModel Getter(CliTypeIdentity returnType)
    {
        var signature = MethodSignatureModel.Create(returnType);
        var definition = new MethodDefinitionModel(
            new(Assembly, 0x06000001),
            new(Assembly, 0x02000001),
            "get_Value",
            IsStatic: false,
            signature,
            RelativeVirtualAddress: 1);
        return new(definition, Aggregate, [], signature);
    }

    private static CilMethodBody GetterBody(
        MethodInstanceModel getter,
        params CilInstruction[] instructions) => new(
            getter.Definition,
            1,
            [],
            [.. instructions])
        {
            MethodInstance = getter,
            LocalSignatureTypes = [],
        };

    public enum GetterShape
    {
        Unresolved,
        Static,
        Virtual,
        Abstract,
        NoBody,
        WrongDeclaringType,
        Parameterized,
        ValueReturn,
        ExceptionRegion,
        WrongBody,
        WrongReturnType,
    }

    private sealed class FixedMethodBodyReader(CilMethodBody body) : IMethodBodyReader, ICalleeMethodBodyReader
    {
        public bool TryReadMethodBody(
            MethodDefinitionModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public bool TryReadMethodBody(
            MethodInstanceModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => body;

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => body;
    }

    private sealed class FixedCalledMethodResolver(MethodInstanceModel? method) :
        ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private sealed class FixedFieldRepository(FieldDefinitionModel field) :
        IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key)
        {
            Assert.Equal(field.Key, key);
            return field;
        }
    }

    private sealed class ThrowingFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new InvalidOperationException("Field lookup must not run.");
    }

    private sealed class FixedTypeDefinitionResolver(CliTypeLayoutKind layout) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            new(Assembly, 0x02000001),
            "Fixtures",
            "Aggregate",
            IsValueType: true,
            [],
            [])
        {
            LayoutKind = layout,
        };
    }
}
