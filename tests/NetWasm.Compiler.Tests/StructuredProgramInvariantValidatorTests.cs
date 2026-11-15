// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using StructuredMethod = global::NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class StructuredProgramInvariantValidatorTests
{
    [Fact]
    public void RejectsAReachableBlockMissingFromTheStructuredTree()
    {
        var program = Program(ValidationTestData.StructuredMethod());
        var entry = program.Methods.Values.Single();
        var invalid = program with
        {
            Methods = program.Methods.SetItem(
                entry.Header.Method.Key,
                entry with { Body = StructuredSequence.Empty }),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), invalid));

        Assert.Contains("exactly one owner", exception.Message);
        Assert.Throws<ArgumentNullException>(() =>
            CreateValidator().Validate(null!, program));
        Assert.Throws<ArgumentNullException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new StructuredProgramInvariantValidator(null!, null!));
    }

    [Fact]
    public void RejectsMismatchedDefinitionAndConstructedMethodIdentities()
    {
        var structured = ValidationTestData.StructuredMethod();
        var program = Program(structured);
        var wrongDefinition = new EntityKey(ValidationTestData.Assembly, 0x06000002);
        var definitionMismatch = program with
        {
            Methods = ImmutableDictionary<EntityKey, StructuredMethod>.Empty
                .Add(wrongDefinition, structured),
        };
        var constructedMismatch = program with
        {
            ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty
                .Add("wrong", structured),
        };
        var missingConstructedIdentity = program with
        {
            ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty
                .Add("missing", structured with
                {
                    Header = structured.Header with { MethodInstance = null },
                }),
        };

        Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.Symbols(),
            definitionMismatch));
        Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.Symbols(),
            constructedMismatch));
        Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.Symbols(),
            missingConstructedIdentity));
    }

    [Fact]
    public void RejectsInvalidStructuredSourceInstructionsAndTargets()
    {
        var original = ValidationTestData.StructuredMethod();
        var duplicate = original with
        {
            Header = original.Header with
            {
                Instructions = [
                    new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None()),
                    new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None()),
                ],
            },
        };
        var duplicateException = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(
                ValidationTestData.Symbols(),
                Program(duplicate)));
        Assert.Contains("strictly ordered and unique", duplicateException.Message);

        var invalidExit = original with
        {
            Blocks = original.Blocks.SetItem(
                original.EntryBlock,
                original.Blocks[original.EntryBlock] with
                {
                    Exit = new StructuredBranchExit(0, original.EntryBlock),
                }),
        };
        var exitException = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(
                ValidationTestData.Symbols(),
                Program(invalidExit)));
        Assert.Contains("structured branch does not match", exitException.Message);
    }

    [Fact]
    public void RejectsMissingOrInconsistentInstructionStackFacts()
    {
        var original = ValidationTestData.StructuredMethod();
        var missing = original with
        {
            InstructionEntryStacks = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
        };
        var missingException = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), Program(missing)));
        Assert.Contains("verified entry-stack facts", missingException.Message);

        var block = original.Blocks[original.EntryBlock];
        var oversized = original with
        {
            Header = original.Header with { MaxStack = 0 },
            Blocks = original.Blocks.SetItem(
                block.Id,
                block with { EntryStack = [CliValueKind.I4] }),
        };
        var oversizedException = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), Program(oversized)));
        Assert.Contains("exceeds the method max stack", oversizedException.Message);
    }

    [Fact]
    public void AcceptsFinalStructuredRegionsAndExceptionFacts()
    {
        var original = ValidationTestData.StructuredMethod();
        var block = original.Blocks[original.EntryBlock];
        var group = CreateCatchGroup(block.Id, block.StartOffset, block.EndOffset);
        var valid = original with
        {
            Body = new StructuredSequence([
                new StructuredExceptionRegion(group.Id, null),
            ]),
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(group.Id, group),
        };

        CreateValidator().Validate(ValidationTestData.Symbols(), Program(valid));
        CreateValidator().Validate(
            ValidationTestData.Symbols(),
            Program(valid) with
            {
                ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty.Add(
                    valid.Header.MethodInstance!.CanonicalName,
                    valid),
            });
    }

    [Fact]
    public void RejectsUnknownFinalStructuredBlockReferences()
    {
        var original = ValidationTestData.StructuredMethod();
        var block = original.Blocks[original.EntryBlock];
        var invalid = original with
        {
            Body = new StructuredSequence([
                new StructuredCode(new StructuredBlockOccurrence(
                    block.Id,
                    StructuredBlockRole.Owner)),
                new StructuredCode(new StructuredBlockOccurrence(
                    new StructuredBlockId(99),
                    StructuredBlockRole.Owner)),
            ]),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), Program(invalid)));

        Assert.Contains("unknown block", exception.Message);
    }

    [Fact]
    public void AcceptsExplicitExceptionHandlerFacts()
    {
        var original = ValidationTestData.StructuredMethod();
        var block = original.Blocks[original.EntryBlock];
        var group = CreateCatchGroup(block.Id, block.StartOffset, block.EndOffset);
        var valid = original with
        {
            Body = new StructuredSequence([new StructuredExceptionRegion(group.Id, null)]),
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(group.Id, group),
        };

        CreateValidator().Validate(ValidationTestData.Symbols(), Program(valid));
    }

    [Fact]
    public void RejectsNonCanonicalStructuredBlockIdentity()
    {
        var structured = ValidationTestData.StructuredMethod();
        var block = structured.Blocks[structured.EntryBlock];
        var invalid = structured with
        {
            Body = new StructuredSequence([
                new StructuredCode(new StructuredBlockOccurrence(
                    block.Id,
                    StructuredBlockRole.Owner)),
                new StructuredCode(new StructuredBlockOccurrence(
                    new StructuredBlockId(block.Id.Value + 1),
                    StructuredBlockRole.Owner)),
            ]),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), Program(invalid)));

        Assert.Contains("unknown block", exception.Message);
    }

    [Fact]
    public void RejectsDirectStructuredCoverageMismatch()
    {
        var structured = ValidationTestData.StructuredMethod() with
        {
            Body = StructuredSequence.Empty,
        };
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), Program(structured)));

        Assert.Contains("exactly one owner", exception.Message);
    }

    [Fact]
    public void RejectsInconsistentExceptionHandlerBlockFacts()
    {
        var original = ValidationTestData.StructuredMethod();
        var block = original.Blocks[original.EntryBlock];
        var group = CreateCatchGroup(block.Id, block.StartOffset, block.EndOffset) with
        {
            Clauses = [
                new StructuredExceptionClause(
                    CilExceptionRegionKind.Catch,
                    block.StartOffset + 1,
                    block.EndOffset - block.StartOffset,
                    new EntityKey(ValidationTestData.Assembly, 0x01000001),
                    null,
                    StructuredSequence.Empty,
                    null)
                {
                    HandlerBlock = block.Id,
                },
            ],
        };
        var invalid = original with
        {
            Body = new StructuredSequence([new StructuredExceptionRegion(group.Id, null)]),
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(group.Id, group),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(ValidationTestData.Symbols(), Program(invalid)));

        Assert.Contains("inconsistent handler block", exception.Message);
    }

    private static StructuredExceptionGroup CreateCatchGroup(
        StructuredBlockId block,
        int offset,
        int endOffset) => new(
        new StructuredExceptionGroupId(0),
        null,
        offset,
        endOffset - offset,
        [new StructuredExceptionCode(new StructuredSequence([
            new StructuredCode(new StructuredBlockOccurrence(
                block,
                StructuredBlockRole.Owner)),
        ]))],
        [new StructuredExceptionClause(
            CilExceptionRegionKind.Catch,
            offset,
            endOffset - offset,
            new EntityKey(ValidationTestData.Assembly, 0x01000001),
            null,
            StructuredSequence.Empty,
            null)
        {
            HandlerBlock = block,
        }],
        [],
        null,
        null)
        {
            ProtectedBlocks = [block],
        };

    private static WasmMethodLoweringResult Program(StructuredMethod method) => new(
        ImmutableDictionary<EntityKey, StructuredMethod>.Empty.Add(
            method.Header.Method.Key,
            method),
        []);

    private static IStructuredProgramInvariantValidator CreateValidator()
    {
        using var services = NetWasm.Compiler.ControlFlow.ControlFlowServiceCollectionExtensions
            .AddCompilerControlFlow(new ServiceCollection())
            .AddCompilerValidation()
            .BuildServiceProvider();
        return services.GetRequiredService<IStructuredProgramInvariantValidator>();
    }
}
