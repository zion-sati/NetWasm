// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.DynamicCasts;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Over in-memory bodies: interface casts and reference array stores call the dynamic-cast helpers; an interface call,
// and a delegate over an interface method, keep their original for a receiver whose class implements the interface
// and otherwise dispatch on the receiver's implementation interface: J implements M, K is an implementation interface
// without it.
public sealed class DynamicCastBodyRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("Casts");
    private static readonly CliTypeIdentity Interface = Named("I");
    private static readonly CliTypeIdentity WithMethod = Named("J");
    private static readonly CliTypeIdentity WithoutMethod = Named("K");
    private static readonly CliTypeIdentity Castable = Named("IDynamicInterfaceCastable");
    private static readonly CliTypeIdentity Class = Named("C");
    private static readonly CliTypeIdentity Handle = CliTypeIdentity.Named(Assembly, "Casts", "Handle", true);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly MethodInstanceModel InterfaceMethod = Method(Interface, "M", 0x06000001, false, Void, Int32);
    private static readonly MethodInstanceModel Implementation = Method(WithMethod, "M", 0x06000002, false, Void, Int32);
    private static readonly MethodInstanceModel DelegateConstructor = Method(Named("D"), ".ctor", 0x06000003, false, Void,
        CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
    private static readonly CilOperand None = new CilOperand.None();

    [Fact]
    public void AnInterfaceCallDispatchesOnTheReceiversImplementationInterface()
    {
        // ldarg.0; ldc.i4.1; callvirt I.M; ret
        var rewritten = Rewriter().Rewrite(Body((CilOperation.LoadArgument, new CilOperand.Index(0)),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.CallVirtual, new CilOperand.MethodInstance(InterfaceMethod)),
            (CilOperation.Return, None)));

        var instructions = rewritten.Instructions;
        // The argument, then the receiver, into new locals: the receiver, the argument and the handle.
        Assert.Equal([Interface, Int32, Handle], rewritten.LocalSignatureTypes);
        Assert.Equal([CilOperation.StoreLocal, CilOperation.StoreLocal], instructions[2..4].Select(instruction => instruction.Operation));
        Assert.Equal([new CilOperand.Index(1), new CilOperand.Index(0)], instructions[2..4].Select(instruction => instruction.Operand));
        Assert.Equal(["Implementation", "Is", "M", "Is", "InvalidImplementation", "MissingImplementation"], Calls(instructions));
        Assert.Equal([WithMethod, WithoutMethod], TypeTokens(instructions));
        // The original call stays for a receiver whose class implements the interface; J's call branches past it.
        Assert.Equal(CilOperation.CallVirtual, instructions[^2].Operation);
        var end = Assert.Single(instructions, instruction => instruction.Operation == CilOperation.Branch);
        Assert.Equal(new CilOperand.BranchTarget(instructions[^1].Offset), end.Operand);
        Assert.Equal(6, rewritten.MaxStack);
    }

    // A delegate over an interface method creates its delegate from the candidate's method; one a branch enters in the
    // middle stays as written.
    [Fact]
    public void ADelegateOverAnInterfaceMethodBindsToTheImplementationInterfacesMethod()
    {
        (CilOperation, CilOperand)[] creation =
        [
            (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.Duplicate, None),
            (CilOperation.LoadVirtualFunction, new CilOperand.MethodInstance(InterfaceMethod)),
            (CilOperation.NewObject, new CilOperand.MethodInstance(DelegateConstructor)), (CilOperation.Pop, None),
            (CilOperation.Return, None),
        ];

        var rewritten = Rewriter(WithMethod).Rewrite(Body(creation));
        var entered = Rewriter(WithMethod).Rewrite(Body(
            [(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
                .. creation]));

        var instructions = rewritten.Instructions;
        Assert.Equal([Interface, Handle], rewritten.LocalSignatureTypes);
        Assert.Contains(instructions, instruction =>
            instruction is { Operation: CilOperation.LoadFunction, Operand: CilOperand.MethodInstance { Value: var method } } &&
            method.Equals(Implementation));
        Assert.DoesNotContain("MissingImplementation", Calls(instructions));
        // The original creation for a receiver whose class implements the interface: ldloc; dup; ldvirtftn; newobj.
        var original = instructions.ToList().FindIndex(instruction => instruction.Operation == CilOperation.LoadVirtualFunction);
        Assert.Equal([CilOperation.LoadLocal, CilOperation.Duplicate, CilOperation.LoadVirtualFunction, CilOperation.NewObject],
            instructions[(original - 2)..(original + 2)].Select(instruction => instruction.Operation));
        Assert.DoesNotContain(entered.Instructions, instruction => instruction.Operation == CilOperation.LoadFunction);
    }

    // Interface casts and reference stores call their helpers; a cast to a class or to IDynamicInterfaceCastable, a
    // value-type store and a constrained call stay as written, and a body with nothing to rewrite is returned as it is.
    [Fact]
    public void CastsAndReferenceStoresCallTheirHelpers()
    {
        var rewritten = Rewriter().Rewrite(Body(
            (CilOperation.CastClass, new CilOperand.TypeIdentity(Interface)),
            (CilOperation.IsInstance, new CilOperand.TypeIdentity(Interface)),
            (CilOperation.UnboxAny, new CilOperand.TypeIdentity(Interface)),
            (CilOperation.CastClass, new CilOperand.TypeIdentity(Class)),
            (CilOperation.CastClass, new CilOperand.TypeIdentity(Castable)),
            (CilOperation.StoreArrayElementReference, None),
            (CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Class)),
            (CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32)),
            (CilOperation.Constrained, new CilOperand.TypeIdentity(Class)),
            (CilOperation.CallVirtual, new CilOperand.MethodInstance(InterfaceMethod)),
            (CilOperation.Return, None)));
        var untouched = Body((CilOperation.CastClass, new CilOperand.TypeIdentity(Class)), (CilOperation.Return, None));

        Assert.Equal(["CastClass", "IsInstance", "CastClass", "StoreElement", "StoreElement"], Calls(rewritten.Instructions));
        Assert.Equal([CilOperation.CastClass, CilOperation.CastClass, CilOperation.StoreArrayElement, CilOperation.Constrained,
            CilOperation.CallVirtual], rewritten.Instructions.Where(instruction => instruction.Operation != CilOperation.Call &&
            instruction.Operation != CilOperation.Return).Select(instruction => instruction.Operation));
        Assert.Same(untouched.Body, Rewriter().Rewrite(untouched));
    }

    // What is no interface call, cast or store, or a delegate whose creation a branch enters, stays as written: an
    // unresolved or static call, a call on a class, a call with open method arguments, a cast to an array, an open
    // generic or a value type, a store of an open element, and a delegate over a class method, with another
    // constructor or with open method arguments.
    [Fact]
    public void WhatIsNoDynamicCastStaysAsWritten()
    {
        var open = CliTypeIdentity.GenericParameter(true, 0);
        var classMethod = Method(Class, "N", 0x06000004, false, Void);
        var staticMethod = Method(Interface, "S", 0x06000005, true, Void);
        var genericMethod = new MethodInstanceModel(InterfaceMethod.Definition, Interface, [open], InterfaceMethod.Signature);
        var oneParameter = Method(Named("D"), ".ctor", 0x06000006, false, Void, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        (CilOperation, CilOperand)[][] bodies =
        [
            [(CilOperation.CallVirtual, new CilOperand.Entity(new EntityKey(Assembly, 0x0A000001)))],
            [(CilOperation.CallVirtual, new CilOperand.MethodInstance(staticMethod))],
            [(CilOperation.CallVirtual, new CilOperand.MethodInstance(classMethod))],
            [(CilOperation.CallVirtual, new CilOperand.MethodInstance(genericMethod))],
            [(CilOperation.CastClass, new CilOperand.TypeIdentity(CliTypeIdentity.SzArray(Interface)))],
            [(CilOperation.CastClass, new CilOperand.TypeIdentity(CliTypeIdentity.GenericInstantiation(Named("G"), [open])))],
            [(CilOperation.CastClass, new CilOperand.TypeIdentity(Handle))],
            [(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(open))],
            Creation(classMethod, DelegateConstructor),
            Creation(InterfaceMethod, oneParameter),
            Creation(genericMethod, DelegateConstructor),
            // A branch joins the creation at its newobj.
            [(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
                .. Creation(InterfaceMethod, DelegateConstructor)],
        ];

        Assert.All(bodies, code =>
        {
            var body = Body([.. code, (CilOperation.Return, None)]);
            Assert.Same(body.Body, Rewriter().Rewrite(body));
        });

        static (CilOperation, CilOperand)[] Creation(MethodInstanceModel method, MethodInstanceModel constructor) =>
        [
            (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.Duplicate, None),
            (CilOperation.LoadVirtualFunction, new CilOperand.MethodInstance(method)),
            (CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), (CilOperation.Pop, None),
        ];
    }

    // A cast to a closed generic interface calls its helper too.
    [Fact]
    public void ACastToAClosedGenericInterfaceCallsItsHelper()
    {
        var rewritten = Rewriter().Rewrite(Body(
            (CilOperation.CastClass, new CilOperand.TypeIdentity(CliTypeIdentity.GenericInstantiation(Named("G"), [Int32]))),
            (CilOperation.Return, None)));

        Assert.Equal(["CastClass"], Calls(rewritten.Instructions));
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var types = new Types();
        var helpers = new Helpers();
        var finder = new Finder([]);
        var targets = new Targets();
        Assert.Throws<ArgumentNullException>(() => new DynamicCastBodyRewriter(null!, types, types, finder, targets, helpers, Castable));
        Assert.Throws<ArgumentNullException>(() => new DynamicCastBodyRewriter(types, null!, types, finder, targets, helpers, Castable));
        Assert.Throws<ArgumentNullException>(() => new DynamicCastBodyRewriter(types, types, null!, finder, targets, helpers, Castable));
        Assert.Throws<ArgumentNullException>(() => new DynamicCastBodyRewriter(types, types, types, null!, targets, helpers, Castable));
        Assert.Throws<ArgumentNullException>(() => new DynamicCastBodyRewriter(types, types, types, finder, null!, helpers, Castable));
        Assert.Throws<ArgumentNullException>(() => new DynamicCastBodyRewriter(types, types, types, finder, targets, null!, Castable));
        Assert.Throws<ArgumentNullException>(() => new DynamicCastBodyRewriter(types, types, types, finder, targets, helpers, null!));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    private static DynamicCastBodyRewriter Rewriter(params CliTypeIdentity[] candidates)
    {
        var types = new Types();
        return new DynamicCastBodyRewriter(types, types, types, new Finder(candidates.Length == 0 ? [WithMethod, WithoutMethod] : [.. candidates]),
            new Targets(), new Helpers(), Castable);
    }

    private static string[] Calls(ImmutableArray<CilInstruction> instructions) =>
        [.. instructions.Where(instruction => instruction.Operation == CilOperation.Call)
            .Select(instruction => ((CilOperand.MethodInstance)instruction.Operand).Value.Definition.Name)];

    private static CliTypeIdentity[] TypeTokens(ImmutableArray<CilInstruction> instructions) =>
        [.. instructions.Where(instruction => instruction.Operation == CilOperation.LoadTypeToken)
            .Select(instruction => ((CilOperand.TypeIdentity)instruction.Operand).Value)];

    private static CliTypeIdentity Named(string name) => CliTypeIdentity.Named(Assembly, "Casts", name, false);

    private static MethodInstanceModel Method(CliTypeIdentity owner, string name, int token, bool isStatic, CliTypeIdentity result,
        params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(result, [.. parameters]);
        var definition = new MethodDefinitionModel(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001), name,
            isStatic, signature, 1);
        return new MethodInstanceModel(definition, owner, [], signature);
    }

    private static ManagedMethodBody Body(params (CilOperation Operation, CilOperand Operand)[] code)
    {
        var instance = Method(Class, "Run", 0x06000010, true, Void, Interface);
        var body = new CilMethodBody(instance.Definition, 4, [],
            [.. code.Select((instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))])
        {
            MethodInstance = instance,
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        return new ManagedMethodBody(instance, new ValidatedControlFlowGraph(graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    // Type operands, called methods and definitions straight from the operands: I, J, K, IDynamicInterfaceCastable and
    // every generic instantiation are interfaces.
    private sealed class Types : ITypeOperandResolver, ICalledMethodResolver, ITypeDefinitionResolver
    {
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) =>
            ((CilOperand.TypeIdentity)instruction.Operand).Value;

        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            (instruction.Operand as CilOperand.MethodInstance)?.Value;

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new EntityKey(Assembly, 0x02000002), "Casts", identity.FullName ?? "", identity.IsValueType, [], [])
            {
                IsInterface = identity.Shape == CliTypeShape.GenericInstantiation ||
                    identity.FullName is "Casts.I" or "Casts.J" or "Casts.K" or "Casts.IDynamicInterfaceCastable",
            };
    }

    private sealed class Finder(ImmutableArray<CliTypeIdentity> candidates) : IDynamicImplementationInterfaceFinder
    {
        public ImmutableArray<CliTypeIdentity> Find(CliTypeIdentity interfaceType) => candidates;
    }

    // J implements M; K does not.
    private sealed class Targets : IDispatchTargetResolver
    {
        public DispatchTargetModel? Resolve(MethodInstanceModel declaration, CliTypeIdentity receiver) =>
            receiver.Equals(WithMethod) ? new(receiver, Implementation) : null;
    }

    // Each helper by its name; Implementation returns the handle.
    private sealed class Helpers : IDynamicCastHelperResolver
    {
        public MethodInstanceModel Resolve(string name, ImmutableArray<CliTypeIdentity> methodArguments) =>
            Method(Named("DynamicCastHelpers"), name, 0x06000020, true, name == "Implementation" ? Handle : Void);
    }
}
