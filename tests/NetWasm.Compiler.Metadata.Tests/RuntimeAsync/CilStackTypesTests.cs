// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

// The types of the values the runtime async lowering spills: what each producer says, primitives after arithmetic,
// handlers' and filters' exceptions, and unknown where producers disagree or this does not follow one.
public sealed class CilStackTypesTests
{
    private static readonly AssemblyIdentity Assembly = new("stack-types");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
    private static readonly CliTypeIdentity Point = CliTypeIdentity.Named(Assembly, "", "Point", true);
    private static readonly CliTypeIdentity Holder = CliTypeIdentity.Named(Assembly, "", "Holder", false);

    public static TheoryData<string, CilOperation, string?> Producers => new()
    {
        { "argument", CilOperation.LoadArgument, "Point" },
        { "argument address", CilOperation.LoadArgumentAddress, "&Point" },
        { "this", CilOperation.LoadArgument, "Holder" },
        { "this of a struct", CilOperation.LoadArgument, "&Point" },
        { "local", CilOperation.LoadLocal, "Point" },
        { "local address", CilOperation.LoadLocalAddress, "&Point" },
        { "int", CilOperation.LoadInt32, "i4" },
        { "compare", CilOperation.CompareEqual, "i4" },
        { "long", CilOperation.LoadInt64, "i8" },
        { "float", CilOperation.LoadFloat32, "f4" },
        { "double", CilOperation.LoadFloat64, "f8" },
        { "native", CilOperation.ConvertNativeInt, "nativeint" },
        { "null", CilOperation.LoadNull, "object" },
        { "field", CilOperation.LoadField, "Point" },
        { "field address", CilOperation.LoadFieldAddress, "&Point" },
        { "call", CilOperation.Call, "Point" },
        { "new", CilOperation.NewObject, "Holder" },
        { "cast", CilOperation.CastClass, "Point" },
        { "is value", CilOperation.IsInstance, "object" },
        { "is reference", CilOperation.IsInstance, "Holder" },
        { "unbox", CilOperation.Unbox, "&Point" },
        { "array", CilOperation.NewArray, "Point[]" },
        { "add", CilOperation.Add, "i4" },
        { "add reference", CilOperation.Add, null },
        { "duplicate", CilOperation.Duplicate, "i4" },
        { "token", CilOperation.LoadTypeToken, null },
        // A field of a type that is not generic, which the decoder names by its definition: its signature's type.
        { "field by key", CilOperation.LoadField, "Point" },
        { "static field by key", CilOperation.LoadStaticField, "Point" },
        { "field address by key", CilOperation.LoadFieldAddress, "&Point" },
        { "field by key unresolved", CilOperation.LoadField, null },
        { "narrowing conversion", CilOperation.ConvertNumeric, "i4" },
        { "long conversion", CilOperation.ConvertNumeric, "i8" },
        { "native conversion", CilOperation.ConvertNumeric, "nativeint" },
    };

    [Theory]
    [MemberData(nameof(Producers))]
    public void TypesWhatEachProducerPushes(string producer, CilOperation operation, string? expected)
    {
        var field = new FieldInstanceModel(new FieldDefinitionModel(new(Assembly, 0x04000001), new(Assembly, 0x02000002),
            "Value", CliValueKind.ValueType, false), Holder, Point);
        var callee = Method("Make", true, new MethodSignatureModel(Point, []));
        var constructor = Method(".ctor", false, new MethodSignatureModel(CliValueKind.Void, []), Holder);
        var feed = producer switch
        {
            "add" or "compare" => [Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(2))],
            "add reference" => [Instruction(CilOperation.LoadNull), Instruction(CilOperation.LoadNull)],
            "native" or "duplicate" => [Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(1))],
            "field" or "field address" or "field by key" or "field address by key" or "field by key unresolved" =>
                [Instruction(CilOperation.LoadNull)],
            "narrowing conversion" or "long conversion" or "native conversion" =>
                [Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(300))],
            "cast" or "is value" or "is reference" or "unbox" or "array" => [Instruction(CilOperation.LoadNull)],
            _ => ImmutableArray<(CilOperation, CilOperand)>.Empty,
        };
        CilOperand operand = producer switch
        {
            "argument" or "argument address" or "local" or "local address" => new CilOperand.Index(producer.StartsWith("arg", StringComparison.Ordinal) ? 1 : 0),
            "this" or "this of a struct" => new CilOperand.Index(0),
            "int" => new CilOperand.ConstantI4(1),
            "long" => new CilOperand.ConstantI8(1),
            "float" => new CilOperand.ConstantF4(1),
            "double" => new CilOperand.ConstantF8(1),
            "field" or "field address" => new CilOperand.FieldInstance(field),
            "field by key" or "static field by key" or "field address by key" or "field by key unresolved" =>
                new CilOperand.Entity(field.Definition.Key),
            "narrowing conversion" => new CilOperand.NumericConversion(8, true, false, false, false),
            "long conversion" => new CilOperand.NumericConversion(64, false, false, false, false),
            "native conversion" => new CilOperand.NumericConversion(64, false, false, false, true),
            "call" => new CilOperand.MethodInstance(callee),
            "new" => new CilOperand.MethodInstance(constructor),
            "is reference" => new CilOperand.TypeIdentity(Holder),
            "cast" or "is value" or "unbox" or "array" => new CilOperand.TypeIdentity(Point),
            _ => new CilOperand.None(),
        };
        ImmutableArray<(CilOperation, CilOperand)> code = [.. feed, (operation, operand), Instruction(CilOperation.Return)];
        var method = Method("M", false, new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), [Point]),
            producer == "this of a struct" ? Point : null);
        var body = Body(code) with { LocalSignatureTypes = [Point] };

        var stacks = CilStackTypes.Compute(method, body,
            producer == "field by key unresolved" ? null : new Fields(field.Definition with { SignatureType = Point }));

        var top = stacks![code.Length - 1][^1];
        Assert.Equal(expected, top is null ? null : Name(top));
    }

    [Fact]
    public void JoinsHandlersFiltersAndBranches()
    {
        var method = Method("M", true, new MethodSignatureModel(CliValueKind.Void, []));
        var body = Body([
            Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),           // 0
            Instruction(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),      // 1
            Instruction(CilOperation.LoadNull),                                           // 2
            Instruction(CilOperation.Branch, new CilOperand.BranchTarget(5)),            // 3
            Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),           // 4
            Instruction(CilOperation.Pop),                                                // 5: object or int: unknown
            Instruction(CilOperation.Leave, new CilOperand.BranchTarget(12)),            // 6
            Instruction(CilOperation.Pop),                                                // 7: filter's exception
            Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),           // 8
            Instruction(CilOperation.EndFilter),                                          // 9
            Instruction(CilOperation.Pop),                                                // 10: caught exception
            Instruction(CilOperation.Leave, new CilOperand.BranchTarget(12)),            // 11
            Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),           // 12
            Instruction(CilOperation.Switch, new CilOperand.SwitchTargets([14])),        // 13
            Instruction(CilOperation.Return),                                             // 14
        ]) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Filter, 0, 7, 10, 2, null, 7) { CatchTypeIdentity = Holder }],
        };

        var stacks = CilStackTypes.Compute(method, body)!;

        Assert.Equal([null], stacks[5]);
        Assert.Equal([Object], stacks[7]);
        Assert.Equal([Holder], stacks[10]);
        Assert.Empty(stacks[12]);
        Assert.Empty(stacks[14]);
        Assert.Null(CilStackTypes.Compute(method, Body([Instruction(CilOperation.CallVirtual), Instruction(CilOperation.Return)])));
        Assert.Empty(CilStackTypes.Compute(method, Body([]))!);
        var caught = Body([Instruction(CilOperation.Leave, new CilOperand.BranchTarget(3)), Instruction(CilOperation.Pop),
            Instruction(CilOperation.Leave, new CilOperand.BranchTarget(3)), Instruction(CilOperation.Return)]) with
        {
            ExceptionRegions = [
                new(CilExceptionRegionKind.Catch, 0, 1, 1, 2, null, null) { CatchTypeIdentity = Holder },
                new(CilExceptionRegionKind.Catch, 0, 1, 1, 2, null, null),
            ],
        };
        // One handler for two catches, of a class and of any exception: an object either way.
        Assert.Equal([Object], CilStackTypes.Compute(method, caught)![1]);
    }

    // Where paths meet with different types in one slot: two references (null and a class, two classes) are objects,
    // two primitives of one stack type (a bool local and an int constant) that stack type; a reference and an int, or two
    // value types, stay unknown.
    [Theory]
    [InlineData("null or class", "object")]
    [InlineData("two classes", "object")]
    [InlineData("bool or int", "i4")]
    [InlineData("enum or int", "i4")]
    [InlineData("class or int", null)]
    [InlineData("two structs", null)]
    public void JoinsWhatBothPathsPush(string shape, string? expected)
    {
        var other = CliTypeIdentity.Named(Assembly, "", "Other", false);
        var size = CliTypeIdentity.Named(Assembly, "", "Size", true);
        var boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
        var color = CliTypeIdentity.Named(Assembly, "", "Color", true, CliValueKind.I4);
        (CilOperation, CilOperand) Push(string what) => what switch
        {
            "null" => Instruction(CilOperation.LoadNull),
            "int" => Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            _ => Instruction(CilOperation.LoadLocal, new CilOperand.Index(what switch
            {
                "Holder" => 0, "Other" => 1, "bool" => 2, "Point" => 3, "Color" => 5, _ => 4,
            })),
        };
        var (left, right) = shape switch
        {
            "null or class" => ("null", "Holder"),
            "two classes" => ("Holder", "Other"),
            "bool or int" => ("bool", "int"),
            "enum or int" => ("Color", "int"),
            "class or int" => ("Holder", "int"),
            _ => ("Point", "Size"),
        };
        var method = Method("M", true, new MethodSignatureModel(CliValueKind.Void, []));
        var body = Body([
            Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),           // 0
            Instruction(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),      // 1
            Push(left),                                                                   // 2
            Instruction(CilOperation.Branch, new CilOperand.BranchTarget(5)),            // 3
            Push(right),                                                                  // 4
            Instruction(CilOperation.Pop),                                                // 5: the join
            Instruction(CilOperation.Return),                                             // 6
        ]) with { LocalSignatureTypes = [Holder, other, boolean, Point, size, color] };

        var top = CilStackTypes.Compute(method, body)![5][^1];

        Assert.Equal(expected, top is null ? null : Name(top));
    }

    [Fact]
    public void TypesAnIndirectCallByItsSignature()
    {
        var method = Method("M", true, new MethodSignatureModel(CliValueKind.Void, []));
        var body = Body([
            Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(CilOperation.ConvertNativeInt),
            Instruction(CilOperation.CallIndirect, new CilOperand.CallSite(new MethodSignatureModel(Point, []))),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.Return),
        ]);

        Assert.Equal("Point", Name(CilStackTypes.Compute(method, body)![3][^1]!));
    }

    private sealed class Fields(FieldDefinitionModel field) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => key == field.Key ? field : throw new KeyNotFoundException();
    }

    private static string Name(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.ManagedByReference => "&" + Name(type.ElementType!),
        CliTypeShape.SzArray => Name(type.ElementType!) + "[]",
        CliTypeShape.Primitive => type.CanonicalName["primitive:".Length..],
        _ => type.FullName!,
    };

    private static MethodInstanceModel Method(string name, bool isStatic, MethodSignatureModel signature,
        CliTypeIdentity? declaring = null)
    {
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), name, isStatic,
            signature, 1);
        return new(definition, declaring ?? Holder, [], signature);
    }

    private static (CilOperation, CilOperand) Instruction(CilOperation operation, CilOperand? operand = null) =>
        (operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(ImmutableArray<(CilOperation Operation, CilOperand Operand)> code)
    {
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M", true,
            new MethodSignatureModel(CliValueKind.Void, []), 1);
        return new(definition, 8, [CliValueKind.ValueType],
            [.. code.Select((item, offset) => new CilInstruction(offset, offset + 1, item.Operation, item.Operand))]);
    }
}
