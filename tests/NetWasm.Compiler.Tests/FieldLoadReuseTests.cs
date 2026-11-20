// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A field read again before anything could change it reads the first read's
// value, on every path that reaches it; a store to the field of any object, a
// call, a ref to the slot or the field, or a path without the first read keeps
// the read. A store through a slot, or to a static, is as good as a read when
// the field holds what was stored: not a byte, and not when the slot changed
// before the store took its object. Both arms of an if that store one field
// compute their values and store once where they meet.
public sealed class FieldLoadReuseTests
{
    [Theory]
    [InlineData("return a.X + a.X;", 1)]
    [InlineData("var r = a.X; a.X = 5; return r + a.X;", 1)]
    [InlineData("a.X = 5; return a.X;", 0)]
    [InlineData("a.X = 5; b.X = 6; return a.X;", 1)]
    [InlineData("a.X = 5; Touch(); return a.X;", 1)]
    [InlineData("a.X++; return a.X + a.X;", 1)]
    [InlineData("a.X = 5; a = b; return a.X;", 1)]
    [InlineData("if (c) a.X = 1; return a.X;", 1)]
    [InlineData("a.X = Id(a = b); return a.X;", 1)]
    [InlineData("n.B = 7; return n.B;", 1)]
    [InlineData("n.Next = a; return n.Next.X;", 1)]
    [InlineData("Holder.S = 4; return Holder.S;", 0)]
    [InlineData("var r = a.X; b.X = 3; return r + a.X;", 2)]
    [InlineData("var r = a.X; b.Y = 3; return r + a.X;", 1)]
    [InlineData("var r = a.X; Touch(); return r + a.X;", 2)]
    [InlineData("var r = a.X; if (c) r += a.Y; return r + a.X;", 1)]
    [InlineData("var r = 0; if (c) r = a.X; return r + a.X;", 2)]
    [InlineData("var r = a.X; a = b; return r + a.X;", 2)]
    [InlineData("var r = a.X; Swap(ref a); return r + a.X;", 2)]
    [InlineData("ref var p = ref a.X; p = 3; return a.X + a.X;", 2)]
    [InlineData("return Holder.S + Holder.S;", 1)]
    [InlineData("var r = Holder.S; Holder.S = 4; return r + Holder.S;", 1)]
    public void ReadsReuseOnlyUnchangedValues(string statement, int reads)
    {
        var body = Probe(statement, new());

        Assert.Equal(reads, body.Instructions.Count(static instruction =>
            instruction.Operation is CilOperation.LoadField or CilOperation.LoadStaticField &&
            instruction.Operand is CilOperand.Entity { Key.MetadataToken: 0x04000001 or 0x04000003 or 0x04000004 or 0x04000005 }));
    }

    [Theory]
    [InlineData("if (c) a.X = 1; else a.X = 2; return 0;", 1)]
    [InlineData("if (a.X == 0) a.X = 16; else a.X--; return 0;", 1)]
    [InlineData("if (c) Holder.S = 1; else Holder.S = 2; return 0;", 1)]
    [InlineData("if (c) a.X = 1; else a.Y = 2; return 0;", 2)]
    [InlineData("if (c) a.X = 1; else b.X = 2; return 0;", 2)]
    [InlineData("if (c) a.X = Next(); else a.X = 2; return 0;", 2)]
    public void ArmsStoringOneFieldStoreOnce(string statement, int stores)
    {
        var body = Probe(statement, new());

        Assert.Equal(stores, body.Instructions.Count(static instruction =>
            instruction.Operation is CilOperation.StoreField or CilOperation.StoreStaticField));
    }

    [Fact]
    public void DisabledSettingsKeepEveryReadAndStore()
    {
        var body = Probe("if (a.X == 0) a.X = 16; else a.X--; return a.X + a.X;",
            new(FieldLoadReuse: ManagedFieldLoadReuse.Disabled, ConditionalStoreMerging: ManagedConditionalStoreMerging.Disabled));

        Assert.Equal(4, body.Instructions.Count(static instruction => instruction.Operation == CilOperation.LoadField));
        Assert.Equal(2, body.Instructions.Count(static instruction => instruction.Operation == CilOperation.StoreField));
    }

    // A static read the volatile prefix marks keeps its memory semantics, as a
    // volatile field's reads do, so the repeat reads the field again.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VolatilePrefixedStaticReadsAreNotReused(bool prefixed)
    {
        var field = new FieldDefinitionModel(new(ValidationTestData.Assembly, 0x04000001),
            new(ValidationTestData.Assembly, 0x02000001), "Flag", CliValueKind.I4, true);
        (CilOperation, CilOperand)[] read = prefixed
            ? [(CilOperation.Volatile, new CilOperand.None()), (CilOperation.LoadStaticField, new CilOperand.Entity(field.Key))]
            : [(CilOperation.LoadStaticField, new CilOperand.Entity(field.Key))];
        (CilOperation, CilOperand)[] code = [.. read, .. read, (CilOperation.Add, new CilOperand.None()), (CilOperation.Return, new CilOperand.None())];
        var body = new CilMethodBody(
            new MethodDefinitionModel(new(ValidationTestData.Assembly, 0x06000001), new(ValidationTestData.Assembly, 0x02000001),
                "Twice", true, MethodSignatureModel.Create(CliValueKind.I4), 1),
            2, [], [.. code.Select((instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Item1, instruction.Item2))]);

        var result = new FieldLoadReuser(new ControlFlowGraphBuilderFactory().Create(), new NoCalls(), new OneField(field),
                new FieldStoreOwnerFinder(new StackValueConsumerFinder(new StackEffectResolver(new NoCalls()))), true)
            .Rewrite(body);

        Assert.Equal(prefixed ? 2 : 1, result.Instructions.Count(static instruction => instruction.Operation == CilOperation.LoadStaticField));
    }

    [Fact]
    public void AnOperandThatNamesNoFieldDescribesNone() =>
        Assert.Null(FieldOperand.Describe(new CilOperand.Index(0), new OneField(new(new(ValidationTestData.Assembly, 0x04000001),
            new(ValidationTestData.Assembly, 0x02000001), "Flag", CliValueKind.I4, true))));

    private sealed class NoCalls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => null;
    }

    private sealed class OneField(FieldDefinitionModel field) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => key == field.Key ? field : throw new KeyNotFoundException();
    }

    private static CilMethodBody Probe(string statement, WasmOptimizationOverrides overrides)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("FieldLoadReuse", $$"""
            using System.Runtime.CompilerServices;

            public sealed class Node
            {
                public int X;
                public int Y;
            }

            public static class Holder
            {
                public static int S;
            }

            public sealed class Narrow
            {
                public byte B;
                public Node Next;
            }

            public static class EntryPoint
            {
                // Holder.S is written, so its reads are field reads, not the default an unwritten field folds to.
                public static int Run(int count)
                {
                    Holder.S = count;
                    return Probe(new Node { X = count }, new Node(), count > 2, new Narrow());
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(Node a, Node b, bool c, Narrow n)
                {
                    {{statement}}
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Touch() { }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Next() => 7;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Id(Node node) => 5;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Swap(ref Node node) => node = new Node { X = 9 };
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: overrides));
        return compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Probe").Body;
    }
}
