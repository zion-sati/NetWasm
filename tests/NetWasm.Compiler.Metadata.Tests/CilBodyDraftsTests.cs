// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

// A decoded body read into a draft and laid out again is the same body at consecutive offsets: each instruction keeps
// its operation, operand and source offset, branch and switch targets and region bounds follow their instructions, and
// the locals keep their order, types and pinning.
public sealed class CilBodyDraftsTests
{
    private static readonly string[] Methods = ["Protected", "Filtered", "Dispatch", "Loop", "Pinned", "Uninitialized"];

    [Fact]
    public void DecodedBodiesLayOutAgainUnchanged()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "DraftFixture",
            """
            using System;
            using System.Runtime.CompilerServices;

            public static unsafe class Fixture
            {
                public static int Protected(int value)
                {
                    try { value = checked(value * 3); }
                    catch (OverflowException) { value = -1; }
                    finally { value++; }
                    return value;
                }

                public static int Filtered(int value)
                {
                    try { throw new InvalidOperationException(); }
                    catch (InvalidOperationException) when (value > 2) { return 1; }
                    catch { return 2; }
                }

                public static int Dispatch(int value) => value switch
                {
                    0 => 10, 1 => 11, 2 => 12, 3 => 13, 4 => 14, _ => -1,
                };

                public static int Loop(int[] values)
                {
                    var total = 0;
                    for (var index = 0; index < values.Length; index++)
                    {
                        if (values[index] < 0) continue;
                        total += values[index];
                    }
                    return total;
                }

                public static int Pinned(int[] values)
                {
                    fixed (int* first = values) { return first[0]; }
                }

                [SkipLocalsInit]
                public static int Uninitialized(int value)
                {
                    Span<int> buffer = stackalloc int[4];
                    buffer[0] = value;
                    return buffer[0];
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            allowUnsafe: true);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var finder = MetadataCapabilityTestData.MethodFinder(snapshot);
        var layout = new CilBodyLayout();
        var seen = new HashSet<CilExceptionRegionKind>();
        var shapes = new HashSet<string>();

        foreach (var name in Methods)
        {
            var original = bodies.ReadMethodBody(finder.FindMethod(snapshot.EntryAssemblyIdentity, "Fixture", name));
            var method = original.MethodInstance ?? new MethodInstanceModel(original.Method,
                CliTypeIdentity.Named(snapshot.EntryAssemblyIdentity, "", "Fixture", false), [], original.Method.Signature);

            var laid = layout.Lay(CilBodyDrafts.Read(original).Draft, method, original.MaxStack);

            AssertSameBody(original, laid);
            seen.UnionWith(original.ExceptionRegions.Select(region => region.Kind));
            if (!original.PinnedLocals.IsEmpty) shapes.Add("pinned");
            if (!original.InitializesLocals) shapes.Add("uninitialized");
        }
        Assert.Superset(new HashSet<CilExceptionRegionKind>
        {
            CilExceptionRegionKind.Catch, CilExceptionRegionKind.Filter, CilExceptionRegionKind.Finally,
        }, seen);
        Assert.Equal(["pinned", "uninitialized"], shapes.Order());
    }

    // The decoder lowers a CIL switch into branches, but a rewrite's own dispatch is one.
    [Fact]
    public void AFaultRegionASwitchAndSourceOffsetsSurvive()
    {
        var definition = new MethodDefinitionModel(new(new AssemblyIdentity("draft"), 0x06000001),
            new(new AssemblyIdentity("draft"), 0x02000002), "M", true, new MethodSignatureModel(CliValueKind.Void, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(new AssemblyIdentity("draft"), "", "T", false),
            [], definition.Signature);
        var original = new CilMethodBody(definition, 2, [CliValueKind.I4], [
            new(0, 2, CilOperation.Nop, new CilOperand.None()) { OriginalOffset = 40 },
            new(2, 4, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            new(4, 6, CilOperation.EndFinally, new CilOperand.None()),
            new(6, 8, CilOperation.LoadLocal, new CilOperand.Index(0)),
            new(8, 13, CilOperation.Switch, new CilOperand.SwitchTargets([6, 13, 14])),
            new(13, 14, CilOperation.Nop, new CilOperand.None()),
            new(14, 15, CilOperation.Return, new CilOperand.None()),
        ])
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Fault, 0, 4, 4, 2, null, null)],
            InitializesLocals = false,
        };

        var laid = new CilBodyLayout().Lay(CilBodyDrafts.Read(original).Draft, method, 2);

        AssertSameBody(original, laid);
        Assert.Equal(40, laid.Instructions[0].SourceOffset);
        Assert.Throws<ArgumentNullException>(() => CilBodyDrafts.Read(null!));
        var empty = CilBodyDrafts.Read(original with { Instructions = [], ExceptionRegions = [] });
        Assert.Equal([0], empty.Labels.Keys);
        Assert.Empty(new CilBodyLayout().Lay(empty.Draft, method, 0).Instructions);
    }

    // The layout numbers a local the draft does not declare where it is first used, and names a label marked twice or
    // never.
    [Fact]
    public void LayoutNumbersUndeclaredLocalsAndRejectsBadLabels()
    {
        var definition = new MethodDefinitionModel(new(new AssemblyIdentity("draft"), 0x06000001),
            new(new AssemblyIdentity("draft"), 0x02000002), "M", true, new MethodSignatureModel(CliValueKind.Void, []), 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(new AssemblyIdentity("draft"), "", "T", false),
            [], definition.Signature);
        var declared = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var undeclared = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I8));
        var label = new CilBodyLabel();
        var layout = new CilBodyLayout();

        var laid = layout.Lay(new([declared], [
            CilBodyElements.Load(undeclared), CilBodyElements.Store(undeclared), CilBodyElements.Load(declared),
            CilBodyElements.Op(CilOperation.Return)], []), method, 1);

        Assert.Equal([CliValueKind.I4, CliValueKind.I8], laid.Locals);
        Assert.Equal(new CilOperand.Index(1), laid.Instructions[0].Operand);
        Assert.Throws<InvalidOperationException>(() => layout.Lay(
            new([], [CilBodyElements.Mark(label), CilBodyElements.Mark(label)], []), method, 1));
        Assert.Throws<InvalidOperationException>(() => layout.Lay(
            new([], [CilBodyElements.Branch(CilOperation.Branch, label)], []), method, 1));
    }

    private static void AssertSameBody(CilMethodBody original, CilMethodBody laid)
    {
        var index = original.Instructions.Select((instruction, position) => (instruction.Offset, position))
            .ToDictionary(item => item.Offset, item => item.position);
        index[original.Instructions[^1].NextOffset] = original.Instructions.Length;
        Assert.Equal(original.Instructions.Length, laid.Instructions.Length);
        for (var position = 0; position < original.Instructions.Length; position++)
        {
            var (before, after) = (original.Instructions[position], laid.Instructions[position]);
            Assert.Equal((position, position + 1), (after.Offset, after.NextOffset));
            Assert.Equal(before.Operation, after.Operation);
            Assert.Equal(before.SourceOffset, after.SourceOffset);
            CilOperand expected = before.Operand switch
            {
                CilOperand.BranchTarget target => new CilOperand.BranchTarget(index[target.Offset]),
                CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets([.. targets.Offsets.Select(offset => index[offset])]),
                var operand => operand,
            };
            if (expected is CilOperand.SwitchTargets cases)
                Assert.Equal(cases.Offsets, ((CilOperand.SwitchTargets)after.Operand).Offsets);
            else
                Assert.Equal(expected, after.Operand);
        }
        Assert.Equal(
            original.ExceptionRegions.Select(region => region with
            {
                TryOffset = index[region.TryOffset],
                TryLength = index[region.TryOffset + region.TryLength] - index[region.TryOffset],
                HandlerOffset = index[region.HandlerOffset],
                HandlerLength = index[region.HandlerOffset + region.HandlerLength] - index[region.HandlerOffset],
                FilterOffset = region.FilterOffset is { } filter ? index[filter] : null,
            }),
            laid.ExceptionRegions);
        Assert.Equal(original.Locals, laid.Locals);
        Assert.Equal(original.LocalSignatureTypes, laid.LocalSignatureTypes);
        Assert.Equal(original.PinnedLocals.Order(), laid.PinnedLocals.Order());
        Assert.Equal(original.InitializesLocals, laid.InitializesLocals);
    }
}
