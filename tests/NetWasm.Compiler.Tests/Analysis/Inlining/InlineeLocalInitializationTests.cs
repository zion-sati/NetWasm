// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Inlining;

// An inlined callee with .locals init must find the locals it may read before writing zero at each entry: the analysis
// finds them by forward definite assignment, and the zeroing writes each one's default.
public sealed class InlineeLocalInitializationTests
{
    private static readonly AssemblyIdentity Assembly = new("Inlinee");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Point = CliTypeIdentity.Named(Assembly, "Fixture", "Point", isValueType: true);
    private static readonly CliTypeIdentity Other = CliTypeIdentity.Named(Assembly, "Fixture", "Other", isValueType: true);

    [Fact]
    public void AMethodWithoutLocalsInitOrLocalsNeedsNoZeroing()
    {
        CilInstruction[] reads = [I(0, CilOperation.LoadLocal, Local(0)), I(1, CilOperation.Return)];
        Assert.Empty(InlineeLocalInitialization.MaybeUnassigned(Body([Int32], reads) with { InitializesLocals = false }));
        Assert.Empty(InlineeLocalInitialization.MaybeUnassigned(Body([], [I(0, CilOperation.Return)])));
        Assert.Empty(InlineeLocalInitialization.MaybeUnassigned(Body([Int32], [])));
        Assert.Throws<ArgumentNullException>(() => InlineeLocalInitialization.MaybeUnassigned(null!));
    }

    // A local stored before any read needs nothing; one read first does.
    [Fact]
    public void ALocalReadBeforeItsFirstStoreIsUnassigned()
    {
        var body = Body([Int32, Int32],
        [
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.StoreLocal, Local(0)),
            I(2, CilOperation.LoadLocal, Local(0)),
            I(3, CilOperation.LoadLocal, Local(1)),
            I(4, CilOperation.Add),
            I(5, CilOperation.Return),
        ]);

        Assert.Equal([1], InlineeLocalInitialization.MaybeUnassigned(body));
    }

    // Paths merge to what every one of them assigned: a store on one arm leaves the local unassigned after the join,
    // stores on both arms do not.
    [Theory]
    [InlineData(false, new[] { 0 })]
    [InlineData(true, new int[0])]
    public void PathsJoinToWhatEachAssigned(bool bothArms, int[] expected)
    {
        CilInstruction[] code = bothArms
            ?
            [
                I(0, CilOperation.LoadArgument, Local(0)),
                I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
                I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(3, CilOperation.StoreLocal, Local(0)),
                I(4, CilOperation.Branch, new CilOperand.BranchTarget(7)),
                I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                I(6, CilOperation.StoreLocal, Local(0)),
                I(7, CilOperation.LoadLocal, Local(0)),
                I(8, CilOperation.Return),
            ]
            :
            [
                I(0, CilOperation.LoadArgument, Local(0)),
                I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
                I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(3, CilOperation.StoreLocal, Local(0)),
                I(4, CilOperation.LoadLocal, Local(0)),
                I(5, CilOperation.Return),
            ];

        Assert.Equal(expected, InlineeLocalInitialization.MaybeUnassigned(Body([Int32], code)));
    }

    // A loop whose body reads the local before storing it reads it unassigned on the first pass; the back edge merges
    // the stored state into the header, which narrows nothing further.
    [Fact]
    public void ALoopReadsItsLocalUnassignedOnTheFirstPass()
    {
        var body = Body([Int32],
        [
            I(0, CilOperation.LoadLocal, Local(0)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(2, CilOperation.Add),
            I(3, CilOperation.StoreLocal, Local(0)),
            I(4, CilOperation.LoadArgument, Local(0)),
            I(5, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0)),
            I(6, CilOperation.LoadLocal, Local(0)),
            I(7, CilOperation.Return),
        ]);

        Assert.Equal([0], InlineeLocalInitialization.MaybeUnassigned(body));
    }

    // initobj on the local's address, or a constructor of its type taking it as this after only plain pushes,
    // initializes it whole; an address that goes anywhere else may be read through.
    [Theory]
    [InlineData("initobj", false)]
    [InlineData("constructor", false)]
    [InlineData("constructor of another type", true)]
    [InlineData("constructor reading the local", true)]
    [InlineData("constructor after a computation", true)]
    [InlineData("constructor with another arity", true)]
    [InlineData("static method", true)]
    [InlineData("address escapes", true)]
    [InlineData("address at the end", true)]
    public void AnAddressInitializesOnlyThroughInitobjOrItsConstructor(string shape, bool unassigned)
    {
        CilInstruction[] start = [I(0, CilOperation.LoadLocalAddress, Local(0))];
        CilInstruction[] middle = shape switch
        {
            "initobj" => [I(1, CilOperation.InitializeObject, new CilOperand.TypeIdentity(Point))],
            "constructor" => [I(1, CilOperation.LoadArgument, Local(0)), I(2, CilOperation.Call, Constructor(Point, 1))],
            "constructor of another type" =>
                [I(1, CilOperation.LoadArgument, Local(0)), I(2, CilOperation.Call, Constructor(Other, 1))],
            "constructor reading the local" =>
                [I(1, CilOperation.LoadLocal, Local(0)), I(2, CilOperation.Call, Constructor(Point, 1))],
            "constructor after a computation" =>
                [I(1, CilOperation.LoadArgument, Local(0)), I(2, CilOperation.Negate), I(3, CilOperation.Call, Constructor(Point, 1))],
            "constructor with another arity" => [I(1, CilOperation.Call, Constructor(Point, 1))],
            "static method" => [I(1, CilOperation.Call, Constructor(Point, 0, isStatic: true))],
            "address escapes" => [I(1, CilOperation.Pop)],
            _ => [],
        };
        var tail = shape == "address at the end"
            ? Array.Empty<CilInstruction>()
            : [I(10, CilOperation.LoadLocal, Local(0)), I(11, CilOperation.Return)];
        var body = Body([Point], [.. start, .. middle, .. tail]);

        Assert.Equal(unassigned ? FirstLocal : [], InlineeLocalInitialization.MaybeUnassigned(body));
    }

    // A constructor's other arguments may be any plain pushes, another local's value included; a second address of the
    // local before the call may reach it, and another local's address is no plain push.
    [Theory]
    [InlineData("pushes", false)]
    [InlineData("another local", false)]
    [InlineData("the local's address again", true)]
    [InlineData("another local's address", true)]
    public void AConstructorsArgumentsArePlainPushesNotReachingTheLocal(string shape, bool unassigned)
    {
        CilInstruction[] arguments = shape switch
        {
            "pushes" =>
            [
                I(3, CilOperation.LoadInt64, new CilOperand.ConstantI8(1)), I(4, CilOperation.LoadFloat32, new CilOperand.ConstantF4(1)),
                I(5, CilOperation.LoadFloat64, new CilOperand.ConstantF8(1)), I(6, CilOperation.LoadNull),
                I(7, CilOperation.LoadString, new CilOperand.UserString("x")),
                I(8, CilOperation.LoadStaticField, new CilOperand.Entity(new EntityKey(Assembly, 0x04000001))),
            ],
            "another local" => [I(3, CilOperation.LoadLocal, Local(1))],
            "another local's address" => [I(3, CilOperation.LoadLocalAddress, Local(1))],
            _ => [I(3, CilOperation.LoadLocalAddress, Local(0))],
        };
        // local 1 = 0; ldloca 0; <arguments>; call Point::.ctor; ldloc 0; ret
        var body = Body([Point, Int32],
        [
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.StoreLocal, Local(1)),
            I(2, CilOperation.LoadLocalAddress, Local(0)), .. arguments,
            I(10, CilOperation.Call, Constructor(Point, arguments.Length)),
            I(11, CilOperation.LoadLocal, Local(0)), I(12, CilOperation.Return),
        ]);

        Assert.Equal(unassigned ? FirstLocal : [], InlineeLocalInitialization.MaybeUnassigned(body));
    }

    // Each local's default, written into the caller's numbering of the callee's locals.
    [Fact]
    public void ZeroingWritesEachKindsDefault()
    {
        CliTypeIdentity[] locals =
        [
            Int32,
            CliTypeIdentity.Primitive("i8", CliValueKind.I8),
            CliTypeIdentity.Primitive("f4", CliValueKind.F4),
            CliTypeIdentity.Primitive("f8", CliValueKind.F8),
            CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
            CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false),
            Point,
            CliTypeIdentity.FromStackKind(CliValueKind.V128),
            CliTypeIdentity.ManagedByReference(Int32),
        ];
        var body = Body(locals, [I(0, CilOperation.Return)]);

        var zeroing = InlineeLocalInitialization.Zeroing(body, Enumerable.Range(0, locals.Length), 5).ToArray();

        Assert.Equal(
            [
                (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), (CilOperation.StoreLocal, Local(5)),
                (CilOperation.LoadInt64, new CilOperand.ConstantI8(0)), (CilOperation.StoreLocal, Local(6)),
                (CilOperation.LoadFloat32, new CilOperand.ConstantF4(0)), (CilOperation.StoreLocal, Local(7)),
                (CilOperation.LoadFloat64, new CilOperand.ConstantF8(0)), (CilOperation.StoreLocal, Local(8)),
                (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), (CilOperation.ConvertNativeInt, new CilOperand.None()),
                (CilOperation.StoreLocal, Local(9)),
                (CilOperation.LoadNull, new CilOperand.None()), (CilOperation.StoreLocal, Local(10)),
                (CilOperation.LoadLocalAddress, Local(11)), (CilOperation.InitializeObject, new CilOperand.TypeIdentity(Point)),
                (CilOperation.LoadLocalAddress, Local(12)),
                (CilOperation.InitializeObject, new CilOperand.TypeIdentity(locals[7])),
            ],
            zeroing);
        Assert.Throws<ArgumentNullException>(() => InlineeLocalInitialization.Zeroing(null!, [], 0).ToArray());
        Assert.Throws<ArgumentNullException>(() => InlineeLocalInitialization.Zeroing(body, null!, 0).ToArray());
    }

    private static CilMethodBody Body(CliTypeIdentity[] locals, CilInstruction[] instructions) =>
        new(Method("Callee", 0, isStatic: true), 4, [.. locals.Select(static type => type.StackKind)], [.. instructions])
        {
            LocalSignatureTypes = [.. locals],
            InitializesLocals = true,
        };

    private static MethodDefinitionModel Method(string name, int parameters, bool isStatic) => new(
        new(Assembly, 0x06000001 + parameters), new(Assembly, 0x02000001), name, isStatic,
        MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), [.. Enumerable.Repeat(Int32, parameters)]), 1);

    private static readonly int[] FirstLocal = [0];

    private static CilOperand.MethodInstance Constructor(CliTypeIdentity type, int parameters, bool isStatic = false)
    {
        var definition = Method(isStatic ? "Create" : ".ctor", parameters, isStatic);
        return new CilOperand.MethodInstance(new MethodInstanceModel(definition, type, [], definition.Signature));
    }

    private static CilOperand.Index Local(int index) => new(index);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());
}
