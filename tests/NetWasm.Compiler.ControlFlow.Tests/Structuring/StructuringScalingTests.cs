// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// Methods of many loops and handlers structure in work proportional to them: a loop in a try and one in its catch, a
// try in each loop, and loops left only by a leave out of their try. Asking each loop about the method's every block,
// domain or region (subset tests that copied their argument, a reserved set rebuilt at every step, scans of every
// region) took the loops times the method; allocation measures it on the thread that structures.
public sealed class StructuringScalingTests
{
    [Theory]
    [InlineData("handlers")]
    [InlineData("nested")]
    [InlineData("leave")]
    public void StructuringAllocatesInProportionToTheLoops(string shape)
    {
        var small = Allocated(shape, 500);
        var large = Allocated(shape, 1_000);

        Assert.True(large < small * 3, $"{shape}: 500 loops {small} bytes; 1,000: {large}");
    }

    private static long Allocated(string shape, int count)
    {
        var body = shape switch
        {
            "handlers" => Handlers(count),
            "nested" => Nested(count),
            _ => Leaving(count),
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = Structurize(body);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // try { for (a = 0; a < arg; a++) ; } catch { for (c = 0; c < arg; c++) ; }, each with locals of its own.
    private static CilMethodBody Handlers(int count)
    {
        var instructions = new List<CilInstruction>();
        var regions = new List<CilExceptionRegion>();
        for (var index = 0; index < count; index++)
        {
            var start = index * 24;
            Loop(instructions, start, index * 2);
            instructions.Add(I(start + 10, CilOperation.Leave, new CilOperand.BranchTarget(start + 23)));
            instructions.Add(I(start + 11, CilOperation.Pop));
            Loop(instructions, start + 12, index * 2 + 1);
            instructions.Add(I(start + 22, CilOperation.Leave, new CilOperand.BranchTarget(start + 23)));
            instructions.Add(I(start + 23, CilOperation.Nop));
            regions.Add(new(CilExceptionRegionKind.Catch, start, 11, start + 11, 12, TypeKey, null));
        }
        return Finish(instructions, regions, count * 24, count * 2);
    }

    // for (a = 0; a < arg; a++) { try { } catch { } }
    private static CilMethodBody Nested(int count)
    {
        var instructions = new List<CilInstruction>();
        var regions = new List<CilExceptionRegion>();
        for (var index = 0; index < count; index++)
        {
            var start = index * 16;
            var counter = new CilOperand.Index(index);
            instructions.AddRange(
                I(start, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(start + 1, CilOperation.StoreLocal, counter),
                I(start + 2, CilOperation.Branch, new CilOperand.BranchTarget(start + 13)),
                I(start + 3, CilOperation.Nop),
                I(start + 4, CilOperation.LoadLocal, counter),
                I(start + 5, CilOperation.Pop),
                I(start + 6, CilOperation.Leave, new CilOperand.BranchTarget(start + 9)),
                I(start + 7, CilOperation.Pop),
                I(start + 8, CilOperation.Leave, new CilOperand.BranchTarget(start + 9)),
                I(start + 9, CilOperation.LoadLocal, counter),
                I(start + 10, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(start + 11, CilOperation.Add),
                I(start + 12, CilOperation.StoreLocal, counter),
                I(start + 13, CilOperation.LoadLocal, counter),
                I(start + 14, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(start + 15, CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(start + 3)));
            regions.Add(new(CilExceptionRegionKind.Catch, start + 4, 3, start + 7, 2, TypeKey, null));
        }
        return Finish(instructions, regions, count * 16, count);
    }

    // while (true) { try { if (arg > k) break; } catch { } }
    private static CilMethodBody Leaving(int count)
    {
        var instructions = new List<CilInstruction>();
        var regions = new List<CilExceptionRegion>();
        for (var index = 0; index < count; index++)
        {
            var start = index * 8;
            instructions.AddRange(
                I(start, CilOperation.Nop),
                I(start + 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(start + 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(index)),
                I(start + 3, CilOperation.BranchIfLessThanOrEqualSigned, new CilOperand.BranchTarget(start + 5)),
                I(start + 4, CilOperation.Leave, new CilOperand.BranchTarget(start + 8)),
                I(start + 5, CilOperation.Leave, new CilOperand.BranchTarget(start)),
                I(start + 6, CilOperation.Pop),
                I(start + 7, CilOperation.Leave, new CilOperand.BranchTarget(start)));
            regions.Add(new(CilExceptionRegionKind.Catch, start + 1, 5, start + 6, 2, TypeKey, null));
        }
        return Finish(instructions, regions, count * 8, 0);
    }

    // for (local = 0; local < arg; local++) ; in ten instructions from the start.
    private static void Loop(List<CilInstruction> instructions, int start, int local)
    {
        var counter = new CilOperand.Index(local);
        instructions.AddRange(
            I(start, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(start + 1, CilOperation.StoreLocal, counter),
            I(start + 2, CilOperation.Branch, new CilOperand.BranchTarget(start + 7)),
            I(start + 3, CilOperation.LoadLocal, counter),
            I(start + 4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(start + 5, CilOperation.Add),
            I(start + 6, CilOperation.StoreLocal, counter),
            I(start + 7, CilOperation.LoadLocal, counter),
            I(start + 8, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(start + 9, CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(start + 3)));
    }

    private static CilMethodBody Finish(List<CilInstruction> instructions, List<CilExceptionRegion> regions, int end, int locals)
    {
        instructions.Add(I(end, CilOperation.Return));
        return Body(CliValueKind.Void, 2, [.. Enumerable.Repeat(CliValueKind.I4, locals)], [.. instructions]) with
        {
            ExceptionRegions = [.. regions],
        };
    }
}
