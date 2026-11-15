// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilInteractionGeneratorTests
{
    private static readonly RandomCilMetadataTokens Tokens = new(
        BoxConstructor: 0x06000001,
        BoxValueField: 0x04000001,
        BoxAddMethod: 0x06000002,
        StaticValueField: 0x04000002,
        HelperMethod: 0x06000003,
        HelperUnaryMethod: 0x06000004,
        UnaryCallSite: 0x06000005,
        UserString: 0x70000001,
        GetHashCodeMethod: 0x06000006,
        Int32Type: 0x02000001,
        BoxType: 0x02000002,
        InterfaceAddMethod: 0x06000007,
        ArrayInitializerField: 0x04000003,
        InitializeArrayMethod: 0x0a000002,
        StaticTransformType: 0x02000003,
        StaticApplyMethod: 0x06000008,
        DefaultBoxConstructor: 0x06000009,
        DefaultAddMethod: 0x0600000a,
        DerivedBoxConstructor: 0x0600000b,
        BaseCopyMethod: 0x0600000c,
        BaseReadMethod: 0x0600000d,
        ExceptionType: 0x01000002,
        RangeSliceMethod: 0x0600000e);

    [Fact]
    public void EveryDeclaredLevelProducesAValidRepresentativeProgram()
    {
        var matrix = new RandomCilInteractionMatrix();
        var generator = new RandomCilInteractionGenerator();
        var validator = new GeneratedCilValidator();
        var baseline = matrix.Dimensions.ToImmutableDictionary(
            dimension => dimension.Name,
            dimension => dimension.Levels[^1],
            StringComparer.Ordinal);

        foreach (var dimension in matrix.Dimensions)
        {
            foreach (var level in dimension.Levels)
            {
                validator.Validate(generator.Generate(
                    new RandomCilInteractionCase(
                        $"representative-{dimension.Name}-{level}",
                        matrix.Seeds[0],
                        baseline.SetItem(dimension.Name, level)),
                    Tokens));
            }
        }
    }

    [Fact]
    public void EveryDeclaredDimensionChangesTheGeneratedProgram()
    {
        var matrix = new RandomCilInteractionMatrix();
        var generator = new RandomCilInteractionGenerator();
        var baseline = matrix.GenerateExhaustive()[0] with
        {
            Levels = matrix.Dimensions.ToImmutableDictionary(
                dimension => dimension.Name,
                dimension => dimension.Levels[^1],
                StringComparer.Ordinal),
        };

        foreach (var dimension in matrix.Dimensions)
        {
            var fingerprints = dimension.Levels
                .Select(level => baseline with
                {
                    Levels = baseline.Levels.SetItem(dimension.Name, level),
                })
                .Select(testCase => Fingerprint(generator.Generate(testCase, Tokens)))
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(
                fingerprints.Count == dimension.Levels.Length,
                $"Dimension '{dimension.Name}' declared {dimension.Levels.Length} levels " +
                $"but generated only {fingerprints.Count} distinct programs.");
        }
    }

    [Fact]
    public void StaticAbstractInterfaceCallInteractsWithControlFlowAndLifetime()
    {
        var matrix = new RandomCilInteractionMatrix();
        var levels = matrix.Dimensions.ToImmutableDictionary(
            dimension => dimension.Name,
            dimension => dimension.Levels[0],
            StringComparer.Ordinal)
            .SetItem("call", "static-interface")
            .SetItem("cfg", "backedge")
            .SetItem("eh", "finally")
            .SetItem("lifetime", "reference-across-allocation");
        var testCase = new RandomCilInteractionCase("static-interface-focused", 17, levels);

        var program = new RandomCilInteractionGenerator().Generate(testCase, Tokens);
        new GeneratedCilValidator().Validate(program);

        var instructions = program.Blocks.SelectMany(block => block.Instructions).ToArray();
        var prefix = Array.FindIndex(
            instructions,
            instruction => instruction.Operation == CilOperation.Constrained);
        Assert.True(prefix >= 0);
        Assert.Equal(CilOperation.Call, instructions[prefix + 1].Operation);
    }

    [Theory]
    [InlineData("default-interface")]
    [InlineData("covariant-return")]
    public void ModernDispatchCallsInteractWithControlFlowAndLifetime(string call)
    {
        var matrix = new RandomCilInteractionMatrix();
        var levels = matrix.Dimensions.ToImmutableDictionary(
            dimension => dimension.Name,
            dimension => dimension.Levels[0],
            StringComparer.Ordinal)
            .SetItem("call", call)
            .SetItem("cfg", "overlap")
            .SetItem("eh", "finally")
            .SetItem("lifetime", "reference-across-allocation");
        var testCase = new RandomCilInteractionCase(call + "-focused", 19, levels);

        var program = new RandomCilInteractionGenerator().Generate(testCase, Tokens);
        new GeneratedCilValidator().Validate(program);

        Assert.Contains(
            program.Blocks.SelectMany(block => block.Instructions),
            instruction => instruction.Operation == CilOperation.CallVirtual);
    }

    [Fact]
    public void UnsignedFiniteAndAlignmentPrefixesInteractWithEhAndMemory()
    {
        var matrix = new RandomCilInteractionMatrix();
        var levels = matrix.Dimensions.ToImmutableDictionary(
            dimension => dimension.Name,
            dimension => dimension.Levels[0],
            StringComparer.Ordinal)
            .SetItem("numeric", "unsigned-finite")
            .SetItem("call", "direct")
            .SetItem("data", "array")
            .SetItem("eh", "finally");
        var testCase = new RandomCilInteractionCase("prefix-focused", 23, levels);

        var program = new RandomCilInteractionGenerator().Generate(testCase, Tokens);
        new GeneratedCilValidator().Validate(program);
        var operations = program.Blocks.SelectMany(block => block.Instructions)
            .Select(instruction => instruction.Operation)
            .ToHashSet();

        Assert.Contains(CilOperation.ConvertFloatUnsigned, operations);
        Assert.Contains(CilOperation.CheckFinite, operations);
        Assert.Contains(CilOperation.Unaligned, operations);
        Assert.Contains(CilOperation.Break, operations);
    }

    [Fact]
    public void FaultCleanupInteractsWithDispatchAndReferenceLifetime()
    {
        var matrix = new RandomCilInteractionMatrix();
        var levels = matrix.Dimensions.ToImmutableDictionary(
            dimension => dimension.Name,
            dimension => dimension.Levels[0],
            StringComparer.Ordinal)
            .SetItem("eh", "fault-catch")
            .SetItem("call", "static-interface")
            .SetItem("lifetime", "reference-across-allocation")
            .SetItem("cfg", "overlap");
        var testCase = new RandomCilInteractionCase("fault-focused", 29, levels);

        var program = new RandomCilInteractionGenerator().Generate(testCase, Tokens);
        new GeneratedCilValidator().Validate(program);

        Assert.Contains(
            program.ExceptionRegions,
            region => region.Kind == CilExceptionRegionKind.Fault);
        Assert.Contains(
            program.ExceptionRegions,
            region => region.Kind == CilExceptionRegionKind.Catch);
    }

    [Fact]
    public void RangeSliceInteractsWithFaultAndVirtualDispatch()
    {
        var matrix = new RandomCilInteractionMatrix();
        var levels = matrix.Dimensions.ToImmutableDictionary(
            dimension => dimension.Name,
            dimension => dimension.Levels[0],
            StringComparer.Ordinal)
            .SetItem("data", "range-slice")
            .SetItem("eh", "fault-catch")
            .SetItem("call", "virtual")
            .SetItem("cfg", "switch");
        var testCase = new RandomCilInteractionCase("range-focused", 31, levels);

        var program = new RandomCilInteractionGenerator().Generate(testCase, Tokens);
        new GeneratedCilValidator().Validate(program);

        Assert.Contains(
            program.Blocks.SelectMany(block => block.Instructions),
            instruction => instruction.Operation == CilOperation.Call &&
                instruction.Operand is GeneratedCilOperand.MetadataToken token &&
                token.Value == Tokens.RangeSliceMethod);
    }

    [Fact]
    public void BackedgeUsesDedicatedBoundedLoopCounter()
    {
        var testCase = new RandomCilInteractionMatrix()
            .GenerateExhaustive()
            .First(item => item.Seed == 0x51a7 &&
                item.Levels["cfg"] == "backedge" &&
                item.Levels["numeric"] == "arithmetic" &&
                item.Levels["stack"] == "empty-join" &&
                item.Levels["eh"] == "none" &&
                item.Levels["data"] == "argument-local" &&
                item.Levels["call"] == "none" &&
                item.Levels["lifetime"] == "none" &&
                item.Levels["order"] == "forward");
        var program = new RandomCilInteractionGenerator().Generate(testCase, Tokens);
        var instructions = program.Blocks.SelectMany(block => block.Instructions);

        Assert.Equal(CliValueKind.I4, program.Locals[2]);
        Assert.Contains(
            instructions,
            instruction => instruction.Operation == CilOperation.StoreLocal &&
                instruction.Operand is GeneratedCilOperand.Index { Value: 2 });
        Assert.Contains(
            instructions,
            instruction => instruction.Operation == CilOperation.LoadLocal &&
                instruction.Operand is GeneratedCilOperand.Index { Value: 2 });
        Assert.Contains(
            instructions,
            instruction => instruction.Operation == CilOperation.Subtract);
        Assert.Contains(
            instructions,
            instruction => instruction.Operation == CilOperation.BranchIfFalse);
    }

    private static string Fingerprint(GeneratedCilProgram program)
    {
        var result = new StringBuilder();

        foreach (var block in program.Blocks)
        {
            result.Append('B').Append(block.Id).Append(':');
            AppendValues(result, block.EntryStack);
            AppendValues(result, block.ExceptionRegionPath);

            foreach (var instruction in block.Instructions)
            {
                result.Append(instruction.Operation).Append('(');
                AppendOperand(result, instruction.Operand);
                result.Append(')');
            }
        }

        foreach (var region in program.ExceptionRegions)
        {
            result.Append('E')
                .Append(region.Id).Append(':')
                .Append(region.Kind).Append(':')
                .Append(region.TryStartBlock).Append(':')
                .Append(region.TryEndBlock).Append(':')
                .Append(region.HandlerStartBlock).Append(':')
                .Append(region.HandlerEndBlock).Append(':')
                .Append(region.FilterStartBlock).Append(':')
                .Append(region.CatchTypeToken).Append(';');
        }

        return result.ToString();
    }

    private static void AppendOperand(StringBuilder result, GeneratedCilOperand operand)
    {
        result.Append(operand.GetType().Name).Append(':');

        switch (operand)
        {
            case GeneratedCilOperand.BlockTargets targets:
                AppendValues(result, targets.BlockIds);
                break;
            case GeneratedCilOperand.MetadataToken token:
                result.Append(token.Value).Append(':')
                    .Append(token.StackKind).Append(':')
                    .Append(token.ReturnType).Append(':')
                    .Append(token.IsVirtual).Append(':');
                AppendValues(result, token.ParameterTypes);
                break;
            default:
                result.Append(operand);
                break;
        }
    }

    private static void AppendValues<T>(StringBuilder result, ImmutableArray<T> values)
    {
        foreach (var value in values)
        {
            result.Append(value).Append(',');
        }

        result.Append(';');
    }
}
