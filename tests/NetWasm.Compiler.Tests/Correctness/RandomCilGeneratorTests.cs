using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilGeneratorTests
{
    private readonly RandomCilGenerator _generator = new();
    private readonly GeneratedCilValidator _validator = new();

    [Fact]
    public void SameSeedProducesTheSamePathIndependentProgramAndBytes()
    {
        var first = _generator.Generate(0x51a7);
        var second = _generator.Generate(0x51a7);
        var serializer = new GeneratedCilSerializer(_validator);

        _validator.Validate(first);
        _validator.Validate(second);

        Assert.Equal(serializer.Serialize(first), serializer.Serialize(second));
        Assert.Equal(
            first.Blocks.Select(block => block.Id),
            second.Blocks.Select(block => block.Id));
        Assert.Equal(first.Blocks.Length,
            first.Blocks.Select(block => block.Id).Distinct().Count());
        Assert.Contains(first.Blocks,
            block => block.EntryStack.AsSpan().SequenceEqual([CliValueKind.I4]));
    }

    [Fact]
    public void FixedPilotSeedsExerciseEveryGeneratedInstructionFamily()
    {
        var observed = RandomCilFixtureFactory.PilotSeeds
            .SelectMany(seed => _generator.Generate(seed).Operations)
            .ToHashSet();
        CilOperation[] expected =
        [
            CilOperation.Add,
            CilOperation.Subtract,
            CilOperation.Multiply,
            CilOperation.AddChecked,
            CilOperation.AddCheckedUnsigned,
            CilOperation.SubtractChecked,
            CilOperation.SubtractCheckedUnsigned,
            CilOperation.MultiplyChecked,
            CilOperation.MultiplyCheckedUnsigned,
            CilOperation.BitwiseAnd,
            CilOperation.BitwiseOr,
            CilOperation.BitwiseXor,
            CilOperation.ShiftLeft,
            CilOperation.ShiftRightSigned,
            CilOperation.ShiftRightUnsigned,
            CilOperation.Divide,
            CilOperation.DivideUnsigned,
            CilOperation.Remainder,
            CilOperation.RemainderUnsigned,
            CilOperation.CompareEqual,
            CilOperation.CompareGreaterThanSigned,
            CilOperation.CompareGreaterThanUnsigned,
            CilOperation.CompareLessThanSigned,
            CilOperation.CompareLessThanUnsigned,
            CilOperation.Negate,
            CilOperation.OnesComplement,
            CilOperation.ConvertNumeric,
            CilOperation.BranchIfTrue,
            CilOperation.BranchIfFalse,
            CilOperation.BranchIfEqual,
            CilOperation.BranchIfNotEqual,
            CilOperation.BranchIfGreaterThanSigned,
            CilOperation.BranchIfGreaterThanUnsigned,
            CilOperation.BranchIfGreaterThanOrEqualSigned,
            CilOperation.BranchIfGreaterThanOrEqualUnsigned,
            CilOperation.BranchIfLessThanSigned,
            CilOperation.BranchIfLessThanUnsigned,
            CilOperation.BranchIfLessThanOrEqualSigned,
            CilOperation.BranchIfLessThanOrEqualUnsigned,
        ];

        Assert.All(expected, operation => Assert.Contains(operation, observed));
    }

    [Fact]
    public void ValidatorRejectsDuplicateBlocksMissingTargetsAndStackUnderflow()
    {
        var valid = _generator.Generate(7);
        _validator.Validate(valid);

        Assert.Throws<InvalidDataException>(() => _validator.Validate(valid with
        {
            Blocks = valid.Blocks.Add(valid.Blocks[0]),
        }));

        var first = valid.Blocks[0];
        var missingTarget = first with
        {
            Instructions = first.Instructions.SetItem(
                first.Instructions.Length - 1,
                new(
                    CilOperation.Branch,
                    new GeneratedCilOperand.BlockTarget(int.MaxValue))),
        };
        Assert.Throws<InvalidDataException>(() => _validator.Validate(valid with
        {
            Blocks = valid.Blocks.SetItem(0, missingTarget),
        }));

        var underflow = first with
        {
            Instructions =
            [
                new(CilOperation.StoreArgument, new GeneratedCilOperand.Index(0)),
                .. first.Instructions,
            ],
        };
        Assert.Throws<InvalidDataException>(() => _validator.Validate(valid with
        {
            Blocks = valid.Blocks.SetItem(0, underflow),
        }));
    }

    [Fact]
    public void SerializerRejectsAnOperationOutsideItsFrozenGenerationProfile()
    {
        var valid = _generator.Generate(3);
        var final = valid.Blocks[^1];
        var unsupported = final with
        {
            Instructions = final.Instructions.Insert(
                0,
                new(CilOperation.LoadString,
                    new GeneratedCilOperand.None())),
        };
        var program = valid with
        {
            Blocks = valid.Blocks.SetItem(valid.Blocks.Length - 1, unsupported),
        };

        Assert.Throws<InvalidDataException>(() =>
            new GeneratedCilSerializer(_validator).Serialize(program));
    }

    [Fact]
    public void ValidatorRejectsLocallocWhenAnotherValueIsLiveOnTheStack()
    {
        var valid = _generator.Generate(3);
        var final = valid.Blocks[^1];
        var invalid = final with
        {
            Instructions = final.Instructions.InsertRange(0,
                System.Collections.Immutable.ImmutableArray.Create<GeneratedCilInstruction>(
                    new(CilOperation.LoadInt32, new GeneratedCilOperand.Int32(1)),
                    new(CilOperation.LoadInt32, new GeneratedCilOperand.Int32(8)),
                    new(CilOperation.LocalAllocate, new GeneratedCilOperand.None()),
                    new(CilOperation.Pop, new GeneratedCilOperand.None()),
                    new(CilOperation.Pop, new GeneratedCilOperand.None()))),
        };

        Assert.Throws<InvalidDataException>(() => _validator.Validate(valid with
        {
            Blocks = valid.Blocks.SetItem(valid.Blocks.Length - 1, invalid),
        }));
    }

    [Fact]
    public void SameSeedAndProfileProduceByteIdenticalPatchedAssemblies()
    {
        var seed = 0x51a7;
        var environment = CompilerCorrectnessEnvironment.Discover();
        var compiler = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        var serializer = new GeneratedCilSerializer(_validator);
        var cil = serializer.Serialize(_generator.Generate(seed));
        var fixture = RandomCilFixtureFactory.Create(seed);
        var root = CorrectnessTestAssets.CreateDirectory();
        var first = compiler.Compile(
            fixture,
            CilProfile.Release,
            Path.Combine(root, "first"));
        var second = compiler.Compile(
            fixture,
            CilProfile.Release,
            Path.Combine(root, "second"));
        var patcher = new MethodBodyPatcher();

        var firstPatched = patcher.Patch(
            first.Desktop.AssemblyPath,
            fixture.EntryType,
            "Run",
            cil,
            maxStack: 8);
        var secondPatched = patcher.Patch(
            second.Desktop.AssemblyPath,
            fixture.EntryType,
            "Run",
            cil,
            maxStack: 8);

        Assert.Equal(firstPatched.AssemblySha256, secondPatched.AssemblySha256);
        Assert.Equal(
            File.ReadAllBytes(firstPatched.AssemblyPath),
            File.ReadAllBytes(secondPatched.AssemblyPath));
    }

    [Fact]
    public void MetadataProfileIsTypeValidDeterministicAndCoversItsFamilies()
    {
        var tokens = new RandomCilMetadataTokens(
            0x06000001,
            0x04000001,
            0x06000002,
            0x04000002,
            0x06000003,
            0x06000004,
            0x11000001,
            0x70000001,
            0x0a000001,
            0x01000001,
            0x02000002,
            0x06000007,
            0x04000003,
            0x0a000002,
            0x02000003,
            0x06000008,
            0x06000009,
            0x0600000a,
            0x0600000b,
            0x0600000c,
            0x0600000d,
            0x01000002,
            0x0600000e);
        var first = _generator.GenerateMetadata(17, tokens);
        var second = _generator.GenerateMetadata(17, tokens);
        var serializer = new GeneratedCilSerializer(_validator);

        _validator.Validate(first);
        _validator.Validate(second);

        Assert.Equal(serializer.Serialize(first), serializer.Serialize(second));
        CilOperation[] expected =
        [
            CilOperation.LoadField,
            CilOperation.LoadFieldAddress,
            CilOperation.StoreField,
            CilOperation.LoadStaticField,
            CilOperation.LoadStaticFieldAddress,
            CilOperation.StoreStaticField,
            CilOperation.LoadTypeToken,
            CilOperation.LoadFieldToken,
            CilOperation.LoadString,
            CilOperation.SizeOf,
            CilOperation.LoadArgumentAddress,
            CilOperation.LoadLocal,
            CilOperation.LoadLocalAddress,
            CilOperation.StoreLocal,
            CilOperation.LoadObject,
            CilOperation.StoreObject,
            CilOperation.CopyObject,
            CilOperation.InitializeObject,
            CilOperation.LocalAllocate,
            CilOperation.CopyBlock,
            CilOperation.InitializeBlock,
            CilOperation.Volatile,
            CilOperation.Readonly,
            CilOperation.Call,
            CilOperation.CallVirtual,
            CilOperation.LoadFunction,
            CilOperation.LoadVirtualFunction,
            CilOperation.CallIndirect,
            CilOperation.Constrained,
            CilOperation.NewObject,
            CilOperation.NewArray,
            CilOperation.LoadArrayLength,
            CilOperation.LoadArrayElementReference,
            CilOperation.StoreArrayElementReference,
            CilOperation.LoadArrayElement,
            CilOperation.LoadArrayElementAddress,
            CilOperation.StoreArrayElement,
            CilOperation.Box,
            CilOperation.Unbox,
            CilOperation.UnboxAny,
            CilOperation.CastClass,
            CilOperation.IsInstance,
        ];
        Assert.All(expected, operation => Assert.Contains(operation, first.Operations));
        Assert.Equal(CliValueKind.ManagedReference, first.Locals[1]);
        Assert.Contains(first.Blocks, block => block.Instructions
            .Select(instruction => instruction.Operation)
            .Take(6)
            .SequenceEqual([
                CilOperation.NewObject,
                CilOperation.StoreLocal,
                CilOperation.NewObject,
                CilOperation.Pop,
                CilOperation.LoadLocal,
                CilOperation.LoadArgument,
            ]));
    }

    [Fact]
    public void ExceptionProfileModelsTypedRegionPathsAndSerializesDeterministically()
    {
        var first = _generator.GenerateExceptionHandling(73);
        var second = _generator.GenerateExceptionHandling(73);
        var serializer = new GeneratedCilSerializer(_validator);

        _validator.Validate(first);
        _validator.Validate(second);
        var firstMethod = serializer.SerializeMethod(first);
        var secondMethod = serializer.SerializeMethod(second);

        Assert.Equal(firstMethod.Cil, secondMethod.Cil);
        Assert.Equal<SerializedGeneratedExceptionRegion>(
            firstMethod.ExceptionRegions,
            secondMethod.ExceptionRegions);
        Assert.Equal(2, first.ExceptionRegions.Length);
        Assert.Contains(first.Blocks, block => block.ExceptionRegionPath.Length == 2);
        Assert.Contains(CilOperation.EndFilter, first.Operations);
        Assert.Contains(CilOperation.EndFinally, first.Operations);
        Assert.Contains(CilOperation.Leave, first.Operations);
        Assert.Contains(CilOperation.Rethrow, first.Operations);
        Assert.Contains(CilOperation.Throw, first.Operations);
    }

    [Fact]
    public void GapDerivedProfileCombinesOverlappingFlowWithNestedCleanup()
    {
        var first = _generator.GenerateControlFlowExceptionCombination(0x61d);
        var second = _generator.GenerateControlFlowExceptionCombination(0x61d);
        var serializer = new GeneratedCilSerializer(_validator);

        _validator.Validate(first);
        _validator.Validate(second);

        var firstMethod = serializer.SerializeMethod(first);
        var secondMethod = serializer.SerializeMethod(second);
        Assert.Equal(firstMethod.Cil, secondMethod.Cil);
        Assert.Equal<SerializedGeneratedExceptionRegion>(
            firstMethod.ExceptionRegions,
            secondMethod.ExceptionRegions);
        Assert.Equal(2, first.ExceptionRegions.Length);
        Assert.All(first.ExceptionRegions,
            region => Assert.Equal(CilExceptionRegionKind.Finally, region.Kind));
        Assert.Contains(first.Blocks,
            block => block.ExceptionRegionPath.Length == 2 &&
                     block.Instructions.Any(instruction =>
                         instruction.Operation == CilOperation.BranchIfTrue));
        Assert.Equal(2, first.Blocks.Count(block => block.Id is 3 or 5));
        Assert.Contains(CilOperation.Leave, first.Operations);
        Assert.Contains(CilOperation.EndFinally, first.Operations);
        Assert.Contains(CilOperation.Add, first.Operations);
        Assert.Contains(CilOperation.Subtract, first.Operations);
    }

    [Fact]
    public void SameExceptionSeedAndProfileProduceByteIdenticalPatchedAssemblies()
    {
        var seed = 73;
        var environment = CompilerCorrectnessEnvironment.Discover();
        var compiler = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        var method = new GeneratedCilSerializer(_validator)
            .SerializeMethod(_generator.GenerateExceptionHandling(seed));
        var fixture = RandomCilFixtureFactory.CreateExceptionHandling(seed);
        var root = CorrectnessTestAssets.CreateDirectory();
        var first = compiler.Compile(
            fixture,
            CilProfile.Release,
            Path.Combine(root, "first-eh"));
        var second = compiler.Compile(
            fixture,
            CilProfile.Release,
            Path.Combine(root, "second-eh"));
        var patcher = new MethodBodyPatcher();

        var firstPatched = patcher.Patch(
            first.Desktop.AssemblyPath,
            fixture.EntryType,
            "Run",
            method,
            maxStack: 8);
        var secondPatched = patcher.Patch(
            second.Desktop.AssemblyPath,
            fixture.EntryType,
            "Run",
            method,
            maxStack: 8);

        Assert.Equal(firstPatched.AssemblySha256, secondPatched.AssemblySha256);
        Assert.Equal(
            File.ReadAllBytes(firstPatched.AssemblyPath),
            File.ReadAllBytes(secondPatched.AssemblyPath));
        AssertPatchedExceptionRegions(firstPatched.AssemblyPath, method);
    }

    [Fact]
    public void SameMetadataSeedAndProfileProduceByteIdenticalPatchedAssemblies()
    {
        var seed = 31;
        var fixture = RandomCilFixtureFactory.Create(seed);
        var (first, second) = CompileFixtureTwice(fixture, "metadata-repeat");
        var resolver = new RandomCilMetadataTokenResolver();
        var serializer = new GeneratedCilSerializer(_validator);
        var firstCil = serializer.Serialize(_generator.GenerateMetadata(
            seed,
            resolver.Resolve(first.Desktop.AssemblyPath)));
        var secondCil = serializer.Serialize(_generator.GenerateMetadata(
            seed,
            resolver.Resolve(second.Desktop.AssemblyPath)));
        var patcher = new MethodBodyPatcher();

        Assert.Equal(firstCil, secondCil);
        var firstPatched = patcher.Patch(
            first.Desktop.AssemblyPath,
            fixture.EntryType,
            "Run",
            firstCil,
            maxStack: 8);
        var secondPatched = patcher.Patch(
            second.Desktop.AssemblyPath,
            fixture.EntryType,
            "Run",
            secondCil,
            maxStack: 8);

        Assert.Equal(firstPatched.AssemblySha256, secondPatched.AssemblySha256);
        Assert.Equal(
            File.ReadAllBytes(firstPatched.AssemblyPath),
            File.ReadAllBytes(secondPatched.AssemblyPath));
    }

    [Fact]
    public void ValidatorRejectsInvalidExceptionRegionIdentityPathAndTerminators()
    {
        var valid = _generator.GenerateExceptionHandling(73);
        _validator.Validate(valid);

        Assert.Throws<InvalidDataException>(() => _validator.Validate(valid with
        {
            ExceptionRegions = valid.ExceptionRegions.Add(valid.ExceptionRegions[0]),
        }));

        var protectedBlockIndex = valid.Blocks
            .Select((block, index) => (block, index))
            .First(item => item.block.ExceptionRegionPath.Any(membership =>
                membership.Part == GeneratedExceptionRegionPart.Try))
            .index;
        var protectedBlock = valid.Blocks[protectedBlockIndex];
        Assert.Throws<InvalidDataException>(() => _validator.Validate(valid with
        {
            Blocks = valid.Blocks.SetItem(protectedBlockIndex, protectedBlock with
            {
                ExceptionRegionPath = [],
            }),
        }));

        var filterEndIndex = valid.Blocks
            .Select((block, index) => (block, index))
            .First(item => item.block.Instructions.Any(instruction =>
                instruction.Operation == CilOperation.EndFilter))
            .index;
        var filterEnd = valid.Blocks[filterEndIndex];
        Assert.Throws<InvalidDataException>(() => _validator.Validate(valid with
        {
            Blocks = valid.Blocks.SetItem(filterEndIndex, filterEnd with
            {
                Instructions = filterEnd.Instructions.SetItem(
                    filterEnd.Instructions.Length - 1,
                    new(CilOperation.EndFinally, new GeneratedCilOperand.None())),
            }),
        }));
    }

    [Fact]
    public void PilotCorpusContainsEveryDirectlySerializableAcceptedOperation()
    {
        var tokens = new RandomCilMetadataTokens(
            0x06000001,
            0x04000001,
            0x06000002,
            0x04000002,
            0x06000003,
            0x06000004,
            0x11000001,
            0x70000001,
            0x0a000001,
            0x01000001,
            0x02000002,
            0x06000007,
            0x04000003,
            0x0a000002,
            0x02000003,
            0x06000008,
            0x06000009,
            0x0600000a,
            0x0600000b,
            0x0600000c,
            0x0600000d,
            0x01000002,
            0x0600000e);
        var observed = RandomCilFixtureFactory.PilotSeeds
            .SelectMany(seed => _generator.Generate(seed).Operations)
            .Concat(_generator.GenerateMetadata(7, tokens).Operations)
            .Concat(_generator.GenerateExceptionHandling(73).Operations)
            .ToHashSet();
        CilOperation[] internalLowered =
        [
            CilOperation.DefaultValue,
            CilOperation.DelegateCombine,
            CilOperation.DelegateRemove,
            CilOperation.DelegateEqual,
            CilOperation.DelegateNotEqual,
            CilOperation.CompareExchange,
            CilOperation.MaterializeType,
            CilOperation.GetObjectType,
            CilOperation.InitializeArrayData,
            CilOperation.NewRectangularArray,
            CilOperation.NewBoundedRectangularArray,
            CilOperation.LoadRectangularArrayElement,
            CilOperation.StoreRectangularArrayElement,
            CilOperation.LoadRectangularArrayElementAddress,
        ];
        var missing = SupportedCil.Operations
            .Except(internalLowered)
            .Where(operation => !observed.Contains(operation))
            .ToArray();

        Assert.Empty(missing);
    }

    private static void AssertPatchedExceptionRegions(
        string assemblyPath,
        SerializedGeneratedMethod expected)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var type = reader.TypeDefinitions
            .Select(handle => (Handle: handle, Definition: reader.GetTypeDefinition(handle)))
            .Single(item => reader.GetString(item.Definition.Name) == "EntryPoint");
        var method = type.Definition.GetMethods()
            .Select(handle => reader.GetMethodDefinition(handle))
            .Single(definition => reader.GetString(definition.Name) == "Run");
        var regions = pe.GetMethodBody(method.RelativeVirtualAddress).ExceptionRegions;

        Assert.Equal(expected.ExceptionRegions.Length, regions.Length);
        for (var index = 0; index < regions.Length; index++)
        {
            var actual = regions[index];
            var serialized = expected.ExceptionRegions[index];
            Assert.Equal(serialized.Kind switch
            {
                CilExceptionRegionKind.Catch => ExceptionRegionKind.Catch,
                CilExceptionRegionKind.Filter => ExceptionRegionKind.Filter,
                CilExceptionRegionKind.Finally => ExceptionRegionKind.Finally,
                CilExceptionRegionKind.Fault => ExceptionRegionKind.Fault,
                _ => throw new InvalidDataException("unexpected generated EH kind"),
            }, actual.Kind);
            Assert.Equal(serialized.TryOffset, actual.TryOffset);
            Assert.Equal(serialized.TryLength, actual.TryLength);
            Assert.Equal(serialized.HandlerOffset, actual.HandlerOffset);
            Assert.Equal(serialized.HandlerLength, actual.HandlerLength);
            if (serialized.FilterOffset is int filterOffset)
            {
                Assert.Equal(filterOffset, actual.FilterOffset);
            }
        }
    }

    private static (CorpusCompilation First, CorpusCompilation Second)
        CompileFixtureTwice(CorpusFixture fixture, string directoryName)
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var compiler = new RoslynCorpusCompiler(
            environment,
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
                new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        var root = Path.Combine(
            CorrectnessTestAssets.CreateDirectory(),
            directoryName);
        return (
            compiler.Compile(
                fixture,
                CilProfile.Release,
                Path.Combine(root, "first")),
            compiler.Compile(
                fixture,
                CilProfile.Release,
                Path.Combine(root, "second")));
    }
}
