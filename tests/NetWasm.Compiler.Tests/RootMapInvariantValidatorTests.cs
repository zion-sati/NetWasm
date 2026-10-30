using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Tests;

public sealed class RootMapInvariantValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyRequiresRootDecisionsForReachableOperations(bool reachable)
    {
        var managed = ValidationTestData.ManagedMethodBody([
            new CilInstruction(0, 1, CilOperation.Branch, new CilOperand.BranchTarget(reachable ? 1 : 3)),
            new CilInstruction(1, 2, CilOperation.NewObject, new CilOperand.None()),
            new CilInstruction(2, 3, CilOperation.Pop, new CilOperand.None()),
            new CilInstruction(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);
        var key = managed.Method.Definition.Key;
        var program = ValidationTestData.Program() with
        {
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(key, managed),
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(key, new(key, [], [])),
        };
        var validator = CreateValidator();

        var exception = Record.Exception(() => validator.Validate(
            ValidationTestData.Types(), ValidationTestData.Fields(), ValidationTestData.Methods(),
            program, new NonDelegateTypeClassifier()));

        if (reachable)
        {
            var failure = Assert.IsType<CompilerException>(exception);
            Assert.Contains("has no root-map decision", failure.Message);
            Assert.Equal(1, failure.Diagnostic.IlOffset);
        }
        else
        {
            Assert.Null(exception);
        }
    }

    [Fact]
    public void RejectsMissingAndNonContiguousRootMapAssignments()
    {
        using var context = CompilerInvariantTestContext.Create();
        var validator = CreateValidator();
        var baseProgram = ValidationTestData.Program();
        var method = baseProgram.EntryPoint!.Key;
        var program = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(method, [], [])),
        };
        var missing = program with
        {
            RootMaps = program.RootMaps.Remove(method),
        };

        var types = MetadataCompilationTestActors.Types(context.Metadata.Snapshot);
        var fields = MetadataCompilationTestActors.Fields(context.Metadata.Snapshot);
        var methods = MetadataCompilationTestActors.Methods(context.Metadata.Snapshot);
        var typeClassifier = MetadataCompilationTestActors.TypeClassifier(context.Metadata.Snapshot);
        var missingException = Assert.Throws<CompilerException>(() =>
            validator.Validate(types, fields, methods, missing, typeClassifier));

        Assert.Contains("root-map keys", missingException.Message);
        var source = new RootSource(RootSourceKind.Local, 0);
        var map = program.RootMaps[method] with
        {
            Slots = new Dictionary<RootSource, int> { [source] = 2 }
                .ToImmutableDictionary(),
        };
        var nonContiguous = program with
        {
            RootMaps = program.RootMaps.SetItem(method, map),
        };
        var slotException = Assert.Throws<CompilerException>(() =>
            validator.Validate(types, fields, methods, nonContiguous, typeClassifier));
        Assert.Contains("not nonnegative and contiguous", slotException.Message);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            null!, fields, methods, program, typeClassifier));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            types, null!, methods, program, typeClassifier));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            types, fields, null!, program, typeClassifier));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            types, fields, methods, null!, typeClassifier));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            types, fields, methods, program, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapInvariantValidator(null!, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapAnalyzerFactory(null!,
                new CalleeArgumentRootingClassifierFactory(new RuntimeAllocationSafepointClassifier()),
                new RootSlotAllocator()));
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapAnalyzerFactory(
                new RootDecisionClassifierFactory(new RuntimeAllocationSafepointClassifier()),
                null!,
                new RootSlotAllocator()));
        Assert.Throws<ArgumentNullException>(() =>
            new RootMapAnalyzerFactory(
                new RootDecisionClassifierFactory(new RuntimeAllocationSafepointClassifier()),
                new CalleeArgumentRootingClassifierFactory(new RuntimeAllocationSafepointClassifier()),
                null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CalleeArgumentRootingClassifierFactory(null!));
    }

    [Fact]
    public void RejectsASafepointWithoutAnExplicitRootDecision()
    {
        using var context = CompilerInvariantTestContext.Create();
        var program = context.Result.Program;
        var selected = program.RootMaps.First(pair => !pair.Value.Safepoints.IsEmpty);
        var offset = selected.Value.Safepoints.Keys.Min();
        var invalid = program with
        {
            RootMaps = program.RootMaps.SetItem(
                selected.Key,
                selected.Value with
                {
                    Safepoints = selected.Value.Safepoints.Remove(offset),
                }),
        };

        var validator = CreateValidator();
        var exception = Assert.Throws<CompilerException>(() =>
            validator.Validate(
                MetadataCompilationTestActors.Types(context.Metadata.Snapshot),
                MetadataCompilationTestActors.Fields(context.Metadata.Snapshot),
                MetadataCompilationTestActors.Methods(context.Metadata.Snapshot),
                invalid,
                MetadataCompilationTestActors.TypeClassifier(context.Metadata.Snapshot)));

        Assert.Contains("has no root-map decision", exception.Message);
        Assert.Equal(offset, exception.Diagnostic.IlOffset);
    }

    [Fact]
    public void RejectsConstructedRootMapKeyMismatches()
    {
        var program = ValidationTestData.Program();
        var managed = program.Methods.Values.Single();
        var invalid = program with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                program.EntryPoint!.Key,
                new MethodRootMap(program.EntryPoint!.Key, [], [])),
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(
                "constructed",
                managed),
        };

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods(),
            invalid,
            new NonDelegateTypeClassifier()));

        Assert.Contains("constructed root-map keys", exception.Message);
    }

    [Fact]
    public void RejectsInvalidRootIdentityAndSafepointBoundaries()
    {
        var baseProgram = ValidationTestData.Program();
        var method = baseProgram.EntryPoint!.Key;
        var repositories = (ValidationTestData.Types(),
            ValidationTestData.Fields(), ValidationTestData.Methods());
        var validator = CreateValidator();

        var identityMismatch = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(
                    new EntityKey(ValidationTestData.Assembly, 0x06000002),
                    [], [])),
        };
        Assert.Contains("method identity", Assert.Throws<CompilerException>(() => validator.Validate(
            repositories.Item1,
            repositories.Item2,
            repositories.Item3,
            identityMismatch,
            new NonDelegateTypeClassifier())).Message);

        var boundaryMismatch = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(
                    method,
                    [],
                    ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                        42,
                        new SafepointRootMap(42, [], [])))),
        };
        Assert.Contains("instruction boundary", Assert.Throws<CompilerException>(() => validator.Validate(
            repositories.Item1,
            repositories.Item2,
            repositories.Item3,
            boundaryMismatch,
            new NonDelegateTypeClassifier())).Message);
    }

    [Fact]
    public void RejectsInvalidSafepointSourcesAndConstructorRoots()
    {
        var baseProgram = ValidationTestData.Program();
        var method = baseProgram.EntryPoint!.Key;
        var source = new RootSource(RootSourceKind.Local, 0);
        var other = new RootSource(RootSourceKind.Local, 1);
        var validator = CreateValidator();

        MethodRootMap Map(SafepointRootMap safepoint) => new(
            method,
            ImmutableDictionary<RootSource, int>.Empty
                .Add(source, 0)
                .Add(other, 1),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0, safepoint));

        var duplicate = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                Map(new SafepointRootMap(0, [source, source], []))),
        };
        Assert.Contains("duplicated", Assert.Throws<CompilerException>(() => validator.Validate(
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods(),
            duplicate,
            new NonDelegateTypeClassifier())).Message);

        var missingSlot = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                Map(new SafepointRootMap(0, [new RootSource(RootSourceKind.Argument, 0)], []))),
        };
        Assert.Contains("no assigned slot", Assert.Throws<CompilerException>(() => validator.Validate(
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods(),
            missingSlot,
            new NonDelegateTypeClassifier())).Message);

        var constructorMismatch = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                Map(new SafepointRootMap(0, [source], [other]))),
        };
        Assert.Contains("preserve every ordinary", Assert.Throws<CompilerException>(() => validator.Validate(
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods(),
            constructorMismatch,
            new NonDelegateTypeClassifier())).Message);

        var ordinaryCollision = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(
                    method,
                    ImmutableDictionary<RootSource, int>.Empty
                        .Add(source, 0)
                        .Add(other, 0),
                    ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                        0,
                        new SafepointRootMap(0, [source, other], [])))),
        };
        Assert.Contains("ordinary safepoint roots share", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.Types(),
                ValidationTestData.Fields(),
                ValidationTestData.Methods(),
                ordinaryCollision,
                new NonDelegateTypeClassifier())).Message);

        var constructorCollision = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(
                    method,
                    ImmutableDictionary<RootSource, int>.Empty
                        .Add(source, 0)
                        .Add(other, 0),
                    ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                        0,
                        new SafepointRootMap(0, [source], [source, other])))),
        };
        Assert.Contains("constructor safepoint roots share", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.Types(),
                ValidationTestData.Fields(),
                ValidationTestData.Methods(),
                constructorCollision,
                new NonDelegateTypeClassifier())).Message);
    }

    [Fact]
    public void AcceptsExplicitDecisionsForUnconditionalSafepoints()
    {
        var managed = ValidationTestData.ManagedMethodBody([
            new CilInstruction(0, 1, CilOperation.NewObject, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()),
        ]);
        var method = managed.Method.Definition;
        var instance = managed.Method;
        var program = new ReachableProgram(
            method,
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(method.Key, managed),
            [method.DeclaringType],
            [], [], [], [], [], [], [], [])
        {
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                instance.CanonicalName,
                instance),
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method.Key,
                new MethodRootMap(
                    method.Key,
                    [],
                    ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                        0,
                        new SafepointRootMap(0, [], [])))),
        };

        CreateValidator().Validate(
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods(),
            program,
            new NonDelegateTypeClassifier());
    }

    [Fact]
    public void AcceptsDirectAndConstructedRootMaps()
    {
        var baseProgram = ValidationTestData.Program();
        var method = baseProgram.EntryPoint!.Key;
        var managed = baseProgram.Methods.Values.Single();
        var constructedKey = "Unit::constructed";
        var valid = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(method, [], [])),
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(
                constructedKey,
                managed),
            ConstructedRootMaps = ImmutableDictionary<string, MethodRootMap>.Empty.Add(
                constructedKey,
                new MethodRootMap(method, [], [])),
        };

        CreateValidator().Validate(
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods(),
            valid,
            new NonDelegateTypeClassifier());

    }

    [Fact]
    public void AcceptsAReusedPhysicalSlotOutsideTheActiveRootSet()
    {
        var baseProgram = ValidationTestData.Program();
        var method = baseProgram.EntryPoint!.Key;
        var active = new RootSource(RootSourceKind.Local, 0);
        var disjoint = new RootSource(RootSourceKind.Local, 1);
        var valid = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(
                    method,
                    ImmutableDictionary<RootSource, int>.Empty
                        .Add(active, 0)
                        .Add(disjoint, 0),
                    ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                        0,
                        new SafepointRootMap(0, [active], [])))),
        };

        CreateValidator().Validate(
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods(),
            valid,
            new NonDelegateTypeClassifier());
    }

    [Fact]
    public void RejectsDirectKeySlotsAndSafepointDecisionsWithoutIntegration()
    {
        var baseProgram = ValidationTestData.Program();
        var method = baseProgram.EntryPoint!.Key;
        var repositories = (
            ValidationTestData.Types(),
            ValidationTestData.Fields(),
            ValidationTestData.Methods());
        var validator = CreateValidator();

        var wrongKeys = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
        };
        Assert.Contains("root-map keys", Assert.Throws<CompilerException>(() => validator.Validate(
            repositories.Item1,
            repositories.Item2,
            repositories.Item3,
            wrongKeys,
            new NonDelegateTypeClassifier())).Message);

        var nonContiguous = baseProgram with
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                method,
                new MethodRootMap(
                    method,
                    ImmutableDictionary<RootSource, int>.Empty.Add(
                        new RootSource(RootSourceKind.Local, 0),
                        2),
                    [])),
        };
        Assert.Contains("not nonnegative and contiguous", Assert.Throws<CompilerException>(() => validator.Validate(
            repositories.Item1,
            repositories.Item2,
            repositories.Item3,
            nonContiguous,
            new NonDelegateTypeClassifier())).Message);

        var managed = ValidationTestData.ManagedMethodBody([
            new CilInstruction(0, 1, CilOperation.NewObject, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()),
        ]);
        var safepointMethod = managed.Method.Definition;
        var safepointProgram = new ReachableProgram(
            safepointMethod,
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(
                safepointMethod.Key,
                managed),
            [safepointMethod.DeclaringType],
            [], [], [], [], [], [], [], [])
        {
            RootMaps = ImmutableDictionary<EntityKey, MethodRootMap>.Empty.Add(
                safepointMethod.Key,
                new MethodRootMap(safepointMethod.Key, [], [])),
        };
        Assert.Contains("no root-map decision", Assert.Throws<CompilerException>(() => validator.Validate(
            repositories.Item1,
            repositories.Item2,
            repositories.Item3,
            safepointProgram,
            new NonDelegateTypeClassifier())).Message);
    }

    private static IRootMapInvariantValidator CreateValidator()
    {
        using var services = new ServiceCollection()
            .AddCompilerGarbageCollection()
            .AddCompilerValidation()
            .BuildServiceProvider();
        return services.GetRequiredService<IRootMapInvariantValidator>();
    }

    private sealed class NonDelegateTypeClassifier : ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => false;
    }
}
