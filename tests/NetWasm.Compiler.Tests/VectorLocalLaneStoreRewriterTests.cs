// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

// The lane stores through a vector variable's address that become stores of its value with the lane replaced, and
// the stores that stay stores to memory because something between the address and the store could write the
// variable. VectorLocalLaneStoreCompilationTests run the shapes against desktop .NET.
public sealed class VectorLocalLaneStoreRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Vector4 = CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector4", isValueType: true);
    private static readonly CliTypeIdentity Vector128 = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true), [Single]);

    [Fact]
    public void AStoreToALaneReplacesTheLaneInTheValue()
    {
        var body = Body(code => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFloat32, 2.0)
            .Op(CilOperation.StoreField, Lane("Y")).Return());

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            [CilOperation.LoadLocal, CilOperation.Call, CilOperation.LoadInt32, CilOperation.LoadFloat32, CilOperation.Call,
                CilOperation.Call, CilOperation.StoreLocal, CilOperation.Return],
            rewritten.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(1), rewritten.Instructions[2].Operand);
        Assert.Equal(["AsVector128", "WithElement", "AsVector4"], Calls(rewritten));
    }

    // C# compiles `v.Y += a` to the lane's address, dup, a read through it, the value and a store through it.
    [Fact]
    public void ACompoundAssignmentThroughTheLanesAddressReadsTheLaneFromTheValue()
    {
        var body = Body(code => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Lane("Y"))
            .Op(CilOperation.Duplicate).Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Single))
            .Op(CilOperation.LoadFloat32, 1.0).Op(CilOperation.Add)
            .Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(Single)).Return());

        var rewritten = Rewriter().Rewrite(body);

        Assert.DoesNotContain(rewritten.Instructions, static instruction => instruction.Operation is
            CilOperation.LoadLocalAddress or CilOperation.LoadFieldAddress or CilOperation.StoreObject);
        Assert.Contains(rewritten.Instructions, static instruction =>
            instruction.Operation == CilOperation.LoadField && instruction.Operand == Lane("Y"));
        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[^2].Operation);
    }

    // A dup of the variable's address whose second copy the next access takes is the address taken again there.
    [Fact]
    public void ADupOfTheAddressSharedByTwoAccessesBecomesTwoAddresses()
    {
        var body = Body(code => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate)
            .Op(CilOperation.LoadFloat32, 5.0).Op(CilOperation.StoreField, Lane("X"))
            .Op(CilOperation.LoadFloat32, 6.0).Op(CilOperation.StoreField, Lane("Z")).Return());

        var rewritten = Rewriter().Rewrite(body);

        Assert.DoesNotContain(rewritten.Instructions, static instruction =>
            instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.Duplicate or CilOperation.StoreField);
        Assert.Equal(2, rewritten.Instructions.Count(static instruction => instruction.Operation == CilOperation.StoreLocal));
    }

    // Between the address and the store the variable is stored, stored through another address, or written by a
    // call through an address the method gave away: the store stays a store through the address.
    [Theory]
    [InlineData("store")]
    [InlineData("address")]
    [InlineData("escaped")]
    public void AStoreTheVariableCouldChangeBeforeStaysAStoreToMemory(string hazard)
    {
        var body = Body(code =>
        {
            if (hazard == "escaped")
            {
                code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Call, Escape());
            }
            code.Local(CilOperation.LoadLocalAddress, 0);
            _ = hazard switch
            {
                "store" => code.Op(CilOperation.DefaultValue, new CilOperand.TypeIdentity(Vector4)).Local(CilOperation.StoreLocal, 0)
                    .Op(CilOperation.LoadFloat32, 1.0),
                "address" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFloat32, 3.0)
                    .Op(CilOperation.StoreField, Lane("X")).Op(CilOperation.LoadFloat32, 1.0),
                _ => code.Op(CilOperation.Call, Next()),
            };
            return code.Op(CilOperation.StoreField, Lane("Y")).Return();
        });

        var rewritten = Rewriter().Rewrite(body);

        Assert.Contains(rewritten.Instructions, static instruction =>
            instruction.Operation == CilOperation.StoreField && instruction.Operand == Lane("Y"));
    }

    // A call between the address and the store cannot reach the variable when the method keeps every address of it.
    [Fact]
    public void ACallBetweenWritesNothingWhenNoAddressEscapes()
    {
        var body = Body(code => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Call, Next())
            .Op(CilOperation.StoreField, Lane("W")).Return());

        Assert.DoesNotContain(Rewriter().Rewrite(body).Instructions,
            static instruction => instruction.Operation == CilOperation.StoreField);
    }

    // A store or read through a lane's address taken just before it, and C#'s `v.X op= a` through a dup of the
    // variable's address, work on the variable's value; so does a store through a vector argument's address.
    [Theory]
    [InlineData("lane store")]
    [InlineData("lane read")]
    [InlineData("dup")]
    [InlineData("argument")]
    [InlineData("overlap")]
    [InlineData("two calls")]
    public void LaneAccessesWorkOnTheValue(string shape)
    {
        var body = shape == "argument"
            ? Body(code => code.Local(CilOperation.LoadArgumentAddress, 0).Op(CilOperation.LoadFloat32, 2.0)
                .Op(CilOperation.StoreField, Lane("Y")).Return(), parameter: Vector4)
            : Body(code => shape switch
            {
                "lane store" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Lane("Y"))
                    .Op(CilOperation.LoadFloat32, 2.0).Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(Single)).Return(),
                "lane read" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Lane("Y"))
                    .Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Single)).Op(CilOperation.Pop).Return(),
                "dup" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate).Op(CilOperation.LoadField, Lane("X"))
                    .Op(CilOperation.LoadFloat32, 1.0).Op(CilOperation.Add).Op(CilOperation.StoreField, Lane("X")).Return(),
                // The dup's lower copy is back on top before the store takes it: the address is taken again there, and
                // the store through the dup, which claims the dup too, waits for the next round.
                "overlap" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate).Op(CilOperation.LoadField, Lane("X"))
                    .Op(CilOperation.Pop).Op(CilOperation.LoadFloat32, 1.0).Op(CilOperation.StoreField, Lane("X")).Return(),
                // Two stores each with a call before them: which addresses escape is found once.
                _ => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Call, Next()).Op(CilOperation.StoreField, Lane("W"))
                    .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Call, Next()).Op(CilOperation.StoreField, Lane("X")).Return(),
            });

        var rewritten = Rewriter().Rewrite(body);

        // A plain lane read through the variable's address (the overlap's) is the dereference folder's to read.
        Assert.DoesNotContain(rewritten.Instructions, static instruction => instruction.Operation is CilOperation.LoadFieldAddress or
            CilOperation.StoreField or CilOperation.StoreObject or CilOperation.LoadObject or CilOperation.Duplicate);
        Assert.All(rewritten.Instructions.Select((instruction, index) => (instruction, index))
                .Where(static pair => pair.instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress),
            pair => Assert.Equal(CilOperation.LoadField, rewritten.Instructions.Skip(pair.index + 1)
                .First(static next => next.Operation != CilOperation.Nop).Operation));
        Assert.Contains(rewritten.Instructions, static instruction => instruction.Operation is CilOperation.StoreLocal or
            CilOperation.StoreArgument or CilOperation.LoadField);
    }

    // What the rewriter cannot follow stays as written: a lane store the variable's own store precedes, through the
    // lane's address or a dup; a dup whose upper copy takes a lane's address; an address that reaches the store from
    // two paths; a store of another type; and dups of the address whose lower copy does not come back alone (a branch
    // joins, no path reaches the next instruction, both copies are consumed or replaced, or the method throws); and a
    // store a call precedes where an address of the variable sits in code no path reaches.
    [Theory]
    [InlineData("lane store disturbed")]
    [InlineData("dup disturbed")]
    [InlineData("dup lane address")]
    [InlineData("two paths")]
    [InlineData("another type")]
    [InlineData("joined")]
    [InlineData("unreachable")]
    [InlineData("both consumed")]
    [InlineData("replaced")]
    [InlineData("thrown")]
    [InlineData("unreachable address")]
    public void WhatTheRewriterCannotFollowStaysAsWritten(string shape)
    {
        var single = new CilOperand.TypeIdentity(Single);
        var body = Body(code => shape switch
        {
            "lane store disturbed" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Lane("Y"))
                .Op(CilOperation.DefaultValue, new CilOperand.TypeIdentity(Vector4)).Local(CilOperation.StoreLocal, 0)
                .Op(CilOperation.LoadFloat32, 1.0).Op(CilOperation.StoreObject, single).Return(),
            "dup disturbed" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate).Op(CilOperation.LoadField, Lane("X"))
                .Op(CilOperation.DefaultValue, new CilOperand.TypeIdentity(Vector4)).Local(CilOperation.StoreLocal, 0)
                .Op(CilOperation.LoadFloat32, 1.0).Op(CilOperation.Add).Op(CilOperation.StoreField, Lane("X")).Return(),
            "dup lane address" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate)
                .Op(CilOperation.LoadFieldAddress, Lane("Y")).Op(CilOperation.LoadObject, single)
                .Op(CilOperation.StoreField, Lane("X")).Return(),
            "two paths" => code.Op(CilOperation.LoadInt32, 0.0).Branch(CilOperation.BranchIfTrue, "other")
                .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Lane("X")).Branch(CilOperation.Branch, "store")
                .Label("other").Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Lane("Y"))
                .Label("store").Op(CilOperation.LoadFloat32, 1.0).Op(CilOperation.StoreObject, single).Return(),
            "another type" => code.Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.LoadInt32, 1.0)
                .Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(Int32)).Local(CilOperation.LoadLocalAddress, 0)
                .Op(CilOperation.Pop).Return(),
            "joined" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate).Op(CilOperation.LoadInt32, 0.0)
                .Branch(CilOperation.BranchIfTrue, "joined").Label("joined").Op(CilOperation.LoadFloat32, 5.0)
                .Op(CilOperation.StoreField, Lane("X")).Op(CilOperation.LoadFloat32, 6.0).Op(CilOperation.StoreField, Lane("Z")).Return(),
            "unreachable" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate).Branch(CilOperation.Branch, "store")
                .Op(CilOperation.Nop).Label("store").Op(CilOperation.LoadFloat32, 5.0).Op(CilOperation.StoreField, Lane("X"))
                .Op(CilOperation.LoadFloat32, 6.0).Op(CilOperation.StoreField, Lane("Z")).Return(),
            "both consumed" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate)
                .Op(CilOperation.CopyObject, new CilOperand.TypeIdentity(Vector4)).Return(),
            "replaced" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate).Op(CilOperation.CompareEqual)
                .Op(CilOperation.Pop).Return(),
            "thrown" => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Duplicate).Op(CilOperation.LoadNull)
                .Op(CilOperation.Throw),
            // An address no path reaches counts as given away, so a call before the store could write the variable.
            _ => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Call, Next()).Op(CilOperation.StoreField, Lane("W"))
                .Branch(CilOperation.Branch, "end").Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadField, Lane("X"))
                .Op(CilOperation.Pop).Label("end").Return(),
        }, locals: shape == "another type" ? [Vector4, Int32] : null);

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Fact]
    public void RejectsMissingDependencies()
    {
        var program = new Program();
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var stacks = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
        var lanes = new VectorLaneFields(program, program);
        var methods = new Methods();
        Assert.Throws<ArgumentNullException>(() => new VectorLocalLaneStoreRewriter(null!, stacks, methods, lanes, true));
        Assert.Throws<ArgumentNullException>(() => new VectorLocalLaneStoreRewriter(graphs, null!, methods, lanes, true));
        Assert.Throws<ArgumentNullException>(() => new VectorLocalLaneStoreRewriter(graphs, stacks, null!, lanes, true));
        Assert.Throws<ArgumentNullException>(() => new VectorLocalLaneStoreRewriter(graphs, stacks, methods, null!, true));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
        var body = Body(code => code.Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFloat32, 2.0)
            .Op(CilOperation.StoreField, Lane("Y")).Return());
        Assert.Same(body, new VectorLocalLaneStoreRewriter(graphs, stacks, methods, lanes, enabled: false).Rewrite(body));
    }

    private static string[] Calls(CilMethodBody body) =>
    [
        .. body.Instructions.Where(static instruction => instruction.Operation == CilOperation.Call)
            .Select(static instruction => ((CilOperand.MethodInstance)instruction.Operand).Value.Definition.Name),
    ];

    private static CilOperand.FieldInstance Lane(string name) =>
        new(new FieldInstanceModel(
            new FieldDefinitionModel(new(Assembly, 0x04000010 + (name[0] - 'W')), new(Assembly, 0x02000010), name, CliValueKind.F4, false)
            {
                SignatureType = Single,
            },
            Vector4,
            Single));

    private static CilOperand.MethodInstance Method(string name, CliTypeIdentity result, params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(result, [.. parameters]);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000100 + name.Length), new(Assembly, 0x02000100), name,
            true, signature, 1);
        return new(new MethodInstanceModel(definition, CliTypeIdentity.Named(Assembly, "Fixture", "Program", isValueType: false),
            [], signature));
    }

    private static CilOperand.MethodInstance Escape() => Method("Escape", CliTypeIdentity.FromStackKind(CliValueKind.Void),
        CliTypeIdentity.ManagedByReference(Vector4));

    private static CilOperand.MethodInstance Next() => Method("Next", Single);

    private static VectorLocalLaneStoreRewriter Rewriter()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program),
            new Methods(), new VectorLaneFields(program, program), enabled: true);
    }

    private static CilMethodBody Body(Func<CilBuilder, CilBuilder> write, CliTypeIdentity? parameter = null,
        CliTypeIdentity[]? locals = null)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", true,
            new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), parameter is null ? [] : [parameter]), 1);
        locals ??= [Vector4];
        return new(method, 8, [.. locals.Select(local => local.StackKind)], write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [.. locals],
        };
    }

    private sealed class Methods : IVectorLoopMethodResolver
    {
        public MethodInstanceModel Resolve(VectorLoopMethod method, CliTypeIdentity lane) => (method.Name switch
        {
            "WithElement" => Method("WithElement", Vector128, Vector128, Int32, Single),
            "AsVector4" => Method("AsVector4", Vector4, Vector128),
            _ => Method(method.Name, Vector128, Vector4),
        }).Value;
    }

    private sealed class Program : ITypeDefinitionResolver, IFieldRepository, ITypeRepository, IMethodRepository
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
