using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using System.Reflection;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Tests;

public sealed class ReachabilityInvariantValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedNativeCallsHaveLoweringTargetsButMissingOrContradictoryRecordsReject(bool dispatch)
    {
        var target = CreateTarget("Native", 0x06000040);
        target = target with
        {
            Definition = target.Definition with
            {
                RelativeVirtualAddress = 0,
                NativeImport = new("mule", "native_call", MethodImportAttributes.CallingConventionCDecl, false, false, null),
            }
        };
        var program = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.Call, new CilOperand.MethodInstance(target)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));
        if (dispatch)
        {
            var site = new DispatchCallSiteModel(program.MethodInstances.Values.Single().CanonicalName,
                0, target, [new(target.DeclaringType, target)]);
            program = program with { DispatchCallSites = program.DispatchCallSites.Add(site.Key, site) };
        }
        var validator = CreateValidator();
        var methods = ValidationTestData.MethodInstances();
        var types = ValidationTestData.TypeClassifier();
        var intrinsics = new EmptyIntrinsicRegistry();
        var reached = program with { MethodInstances = program.MethodInstances.Add(target.CanonicalName, target) };

        validator.Validate(methods, types, reached, intrinsics);
        Assert.Throws<CompilerException>(() => validator.Validate(methods, types, program, intrinsics));
        var contradictory = reached with
        {
            MethodInstances = reached.MethodInstances.SetItem(target.CanonicalName,
            target with { Definition = target.Definition with { NativeImport = target.Definition.NativeImport! with { EntryPoint = "other" } } })
        };
        Assert.Throws<CompilerException>(() => validator.Validate(methods, types, contradictory, intrinsics));
    }

    [Fact]
    public void EntrylessProgramStillRequiresEveryExportToBeReachable()
    {
        var program = CreateUnitProgram();
        var method = Assert.IsType<MethodDefinitionModel>(program.EntryPoint);
        program = program with
        {
            EntryPoint = null,
            Exports = [new ProgramExport("value", method.Key)],
        };
        var validator = CreateValidator();
        var methods = ValidationTestData.MethodInstances();
        var types = ValidationTestData.TypeClassifier();
        var intrinsics = new EmptyIntrinsicRegistry();

        validator.Validate(methods, types, program, intrinsics);
        var exception = Assert.Throws<CompilerException>(() => validator.Validate(
            methods, types, program with { Methods = program.Methods.Remove(method.Key) }, intrinsics));

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains("export 'value' targets an unreachable method", exception.Message);
    }

    [Fact]
    public void UnitContractRejectsEveryTopLevelReachabilityMismatch()
    {
        var validator = CreateValidator();
        var methodInstances = ValidationTestData.MethodInstances();
        var typeClassifier = ValidationTestData.TypeClassifier();
        var program = CreateUnitProgram();
        var intrinsics = new EmptyIntrinsicRegistry();
        var missing = new EntityKey(new AssemblyIdentity("Missing"), 0x06000002);
        var mismatchedBody = CreateManagedMethodBody(missing);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            null!, typeClassifier, program, intrinsics));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            methodInstances, null!, program, intrinsics));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            methodInstances, typeClassifier, null!, intrinsics));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            methodInstances, typeClassifier, program, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ReachabilityInvariantValidator(null!, null!));
        AssertInvariant(program with { Methods = program.Methods.Remove(program.EntryPoint!.Key) });
        AssertInvariant(program with
        {
            Methods = program.Methods.SetItem(program.EntryPoint!.Key, mismatchedBody),
        });
        AssertInvariant(program with
        {
            Exports = [new ProgramExport("missing", missing)],
        });
        AssertInvariant(program with { AllocatingMethods = [missing] });
        AssertInvariant(program with { ConstructedAllocatingMethods = ["missing"] });
        AssertInvariant(program with
        {
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty
                .Add("missing", program.Methods[program.EntryPoint!.Key]),
        });
        AssertInvariant(program with
        {
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add("wrong", program.MethodInstances.Values.Single()),
        });

        void AssertInvariant(ReachableProgram invalid) =>
            Assert.Throws<CompilerException>(() => validator.Validate(
                methodInstances, typeClassifier, invalid, intrinsics));
    }

    [Fact]
    public void RejectsAnEntryPointMissingFromReachability()
    {
        using var context = CompilerInvariantTestContext.Create();
        var program = context.Result.Program;
        var invalid = program with
        {
            Methods = program.Methods.Remove(program.EntryPoint!.Key),
        };

        var snapshot = context.Metadata.Snapshot;
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var typeClassifier = MetadataCompilationTestActors.TypeClassifier(snapshot);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(snapshot);
        var exception = Assert.Throws<CompilerException>(() =>
            CreateValidator().Validate(
                methodInstances,
                typeClassifier,
                invalid,
                CreateIntrinsics(symbols, snapshot.Methods)));

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains("entry point is absent", exception.Message);
    }

    [Fact]
    public void RejectsAReachableCallWhoseLoweringTargetWasOmitted()
    {
        using var context = CompilerInvariantTestContext.Create();
        var program = context.Result.Program;
        var called = program.Methods.Values
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand switch
            {
                CilOperand.MethodInstance method => method.Value.Definition.Key,
                CilOperand.Entity method => method.Key,
                _ => (EntityKey?)null,
            })
            .First(key => key is not null &&
                          key != program.EntryPoint!.Key &&
                          program.Methods.ContainsKey(key.Value) &&
                          !program.AllocatingMethods.Contains(key.Value) &&
                          program.Exports.All(export => export.Method != key.Value));
        var invalid = program with
        {
            Methods = program.Methods.Remove(called!.Value),
        };

        var snapshot = context.Metadata.Snapshot;
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var typeClassifier = MetadataCompilationTestActors.TypeClassifier(snapshot);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(snapshot);
        var exception = Assert.Throws<CompilerException>(() =>
            CreateValidator().Validate(
                methodInstances,
                typeClassifier,
                invalid,
                CreateIntrinsics(symbols, snapshot.Methods)));

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains("has no lowering target", exception.Message);
    }

    [Fact]
    public void RejectsAnUnreachableRuntimeInitializer()
    {
        using var context = CompilerInvariantTestContext.Create();
        var program = context.Result.Program;
        var missing = new EntityKey(new AssemblyIdentity("Missing"), 0x06000001);
        var invalid = program with
        {
            StaticInitializers = [.. program.StaticInitializers, missing],
        };

        var snapshot = context.Metadata.Snapshot;
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var typeClassifier = MetadataCompilationTestActors.TypeClassifier(snapshot);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(snapshot);
        var exception = Assert.Throws<CompilerException>(() =>
            CreateValidator().Validate(
                methodInstances,
                typeClassifier,
                invalid,
                CreateIntrinsics(symbols, snapshot.Methods)));

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains("static initializer", exception.Message);
    }

    [Fact]
    public void RejectsDispatchTargetsThatAreNotReachable()
    {
        var program = ValidationTestData.Program();
        var target = CreateTarget("DispatchTarget", 0x06000002);
        var dispatch = new DispatchCallSiteModel(
            program.MethodInstances.Values.Single().CanonicalName,
            0,
            target,
            [new DispatchTargetModel(target.DeclaringType, target)]);

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program with
            {
                DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                    .Add(dispatch.Key, dispatch),
            },
            new EmptyIntrinsicRegistry()));

        Assert.Contains("targets an unreachable method", exception.Message);
    }

    [Fact]
    public void RejectsConstructedDispatchTargetsThatAreNotReachable()
    {
        var program = ValidationTestData.Program();
        var target = CreateTarget("ConstructedTarget", 0x06000003, constructed: true);
        var dispatch = new DispatchCallSiteModel(
            program.MethodInstances.Values.Single().CanonicalName,
            0,
            target,
            [new DispatchTargetModel(target.DeclaringType, target)]);

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program with
            {
                DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                    .Add(dispatch.Key, dispatch),
            },
            new EmptyIntrinsicRegistry()));

        Assert.Contains("targets an unreachable method", exception.Message);
    }

    [Fact]
    public void RejectsIndirectCallsWithoutCallableTargets()
    {
        var program = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.CallIndirect, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program,
            new EmptyIntrinsicRegistry()));

        Assert.Contains("indirect call has no reachable callable target", exception.Message);
    }

    [Fact]
    public void RejectsFunctionLoadsThatAreNotCallable()
    {
        var target = CreateTarget("LoadedTarget", 0x06000004);
        var program = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.LoadFunction,
                new CilOperand.MethodInstance(target)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program,
            new EmptyIntrinsicRegistry()));

        Assert.Contains("omits callable target", exception.Message);
    }

    [Fact]
    public void RejectsFunctionLoadsThatOmitNativeCallbackTargets()
    {
        var target = CreateTarget("NativeCallback", 0x06000005);
        target = target with
        {
            Definition = target.Definition with
            {
                NativeCallback = new([], null, false, false),
            },
        };
        var program = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.LoadFunction,
                new CilOperand.MethodInstance(target)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program,
            new EmptyIntrinsicRegistry()));

        Assert.Contains("omits native callback target", exception.Message);
    }

    [Fact]
    public void RejectsReachableCallsWithoutMethodOperands()
    {
        var program = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.Call, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));

        var exception = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program,
            new EmptyIntrinsicRegistry()));

        Assert.Contains("has no method operand", exception.Message);
    }

    [Fact]
    public void RejectsUnresolvableCallsAndStaticInitializersWithoutIntegration()
    {
        var target = CreateTarget("Unresolved", 0x06000030);
        var callProgram = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.Call,
                new CilOperand.MethodInstance(target)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));
        var callException = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            callProgram,
            new EmptyIntrinsicRegistry()));
        Assert.Contains("no lowering target", callException.Message);

        var baseProgram = ValidationTestData.Program();
        var initializerException = Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            baseProgram with
            {
                StaticInitializers = [
                    new EntityKey(ValidationTestData.Assembly, 0x06000031),
                ],
            },
            new EmptyIntrinsicRegistry()));
        Assert.Contains("static initializer", initializerException.Message);
    }

    [Fact]
    public void AcceptsReachabilityReferencesAcrossDirectConstructedIntrinsicAndImports()
    {
        var baseProgram = ValidationTestData.Program();
        var caller = baseProgram.MethodInstances.Values.Single();
        var structured = baseProgram.Methods.Values.Single();
        var constructed = CreateTarget("Constructed", 0x06000020, constructed: true);
        var intrinsic = CreateTarget("Intrinsic", 0x06000021);
        var js = CreateTarget("JavaScript", 0x06000022);
        var wit = CreateTarget("Wit", 0x06000023);
        var jsImport = js.Definition with { JSImport = new InteropImportDeclaration("js", "target") };
        var witImport = wit.Definition with { WitImport = new WitImportDeclaration("wit", "target") };
        var dispatch = new DispatchCallSiteModel(
            caller.CanonicalName,
            0,
            caller,
            [
                new DispatchTargetModel(caller.DeclaringType, caller),
                new DispatchTargetModel(constructed.DeclaringType, constructed),
                new DispatchTargetModel(intrinsic.DeclaringType, intrinsic),
                new DispatchTargetModel(js.DeclaringType, js),
                new DispatchTargetModel(wit.DeclaringType, wit),
            ]);
        var constructedDispatch = new DispatchCallSiteModel(
            constructed.CanonicalName,
            1,
            constructed,
            []);
        var constructedTypeTest = new TypeTestSiteModel(
            constructed.CanonicalName,
            1,
            constructed.DeclaringType,
            []);
        var valid = baseProgram with
        {
            Exports = [new ProgramExport("run", caller.Definition.Key)],
            AllocatingMethods = [caller.Definition.Key],
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(
                constructed.CanonicalName,
                structured),
            ConstructedAllocatingMethods = [constructed.CanonicalName],
            ConstructedStaticInitializers = [constructed.CanonicalName],
            StaticInitializers = [caller.Definition.Key],
            Finalizers = ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty.Add(
                caller.DeclaringType,
                caller),
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                .Add(dispatch.Key, dispatch)
                .Add(constructedDispatch.Key, constructedDispatch),
            TypeTestSites = ImmutableDictionary<string, TypeTestSiteModel>.Empty.Add(
                new TypeTestSiteModel(caller.CanonicalName, 0, caller.DeclaringType, []).Key,
                new TypeTestSiteModel(caller.CanonicalName, 0, caller.DeclaringType, []))
                .Add(constructedTypeTest.Key, constructedTypeTest),
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(caller.CanonicalName, caller)
                .Add(constructed.CanonicalName, constructed)
                .Add(intrinsic.CanonicalName, intrinsic)
                .Add(js.CanonicalName, js)
                .Add(wit.CanonicalName, wit),
            MethodInstances = baseProgram.MethodInstances
                .Add(constructed.CanonicalName, constructed),
            JSImportMethods = [jsImport],
            WitImportMethods = [witImport],
        };

        CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            valid,
            new SingleIntrinsicRegistry(intrinsic.Definition.Key));

        var indirect = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.CallIndirect, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None())) with
        {
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(caller.CanonicalName, caller),
        };
        CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            indirect,
            new EmptyIntrinsicRegistry());

        var virtualInstruction = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.CallVirtual,
                new CilOperand.MethodInstance(caller)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None())) with
        {
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                .Add(
                    $"{caller.CanonicalName}@00000000",
                    new DispatchCallSiteModel(
                        caller.CanonicalName,
                        0,
                        caller,
                        [new DispatchTargetModel(caller.DeclaringType, caller)])),
        };
        CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            virtualInstruction,
            new EmptyIntrinsicRegistry());

        var directEntityInstruction = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.Call,
                new CilOperand.Entity(caller.Definition.Key)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));
        CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            directEntityInstruction,
            new EmptyIntrinsicRegistry());
    }

    [Fact]
    public void AcceptsDelegateRuntimeCallsAndCanonicalRuntimeRequirements()
    {
        var caller = ValidationTestData.Program().MethodInstances.Values.Single();
        var delegateTypeKey = new EntityKey(ValidationTestData.Assembly, 0x02000003);
        var delegateType = CliTypeIdentity.Named(
            ValidationTestData.Assembly,
            "",
            "Delegate",
            false);
        var definition = new MethodDefinitionModel(
            new EntityKey(ValidationTestData.Assembly, 0x06000005),
            delegateTypeKey,
            "Invoke",
            false,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var target = new MethodInstanceModel(definition, delegateType, [], definition.Signature);
        var dispatch = new DispatchCallSiteModel(
            caller.CanonicalName,
            99,
            caller,
            []);
        var typeTest = new TypeTestSiteModel(
            caller.CanonicalName,
            100,
            delegateType,
            []);

        var valid = ValidationTestData.Program() with
        {
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                .Add(dispatch.Key, dispatch),
            TypeTestSites = ImmutableDictionary<string, TypeTestSiteModel>.Empty
                .Add(typeTest.Key, typeTest),
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(caller.CanonicalName, caller),
        };

        valid = valid with
        {
            HostCallbacks = [new HostCallbackDeclaration(
                new EntityKey(ValidationTestData.Assembly, 0x06000006),
                0,
                target,
                "callback")],
            JSImportMethods = [new MethodDefinitionModel(
                new EntityKey(ValidationTestData.Assembly, 0x06000006),
                delegateTypeKey,
                "Import",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                0)],
        };

        CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            new DelegateTypeClassifier(),
            valid,
            new EmptyIntrinsicRegistry());

        var delegateCall = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.Call,
                new CilOperand.MethodInstance(target)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));
        CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            new DelegateTypeClassifier(),
            delegateCall,
            new EmptyIntrinsicRegistry());

        var constructor = CreateTarget(".ctor", 0x0600000a);
        var constructorCall = ProgramWithInstructions(
            new CilInstruction(0, 1, CilOperation.Call,
                new CilOperand.MethodInstance(constructor)),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()));
        CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            new DelegateTypeClassifier(),
            constructorCall,
            new EmptyIntrinsicRegistry());
    }

    [Fact]
    public void RejectsInvalidRuntimeRequirementReferences()
    {
        var validator = CreateValidator();
        var baseProgram = ValidationTestData.Program();
        var caller = baseProgram.MethodInstances.Values.Single();
        var missingFinalizer = caller with
        {
            Definition = caller.Definition with
            {
                Key = new EntityKey(ValidationTestData.Assembly, 0x06000007),
                Name = "MissingFinalizer",
            },
        };

        Assert.Contains("constructed static initializer", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.MethodInstances(),
                ValidationTestData.TypeClassifier(),
                baseProgram with { ConstructedStaticInitializers = ["missing"] },
                new EmptyIntrinsicRegistry())).Message);

        Assert.Contains("finalizer", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.MethodInstances(),
                ValidationTestData.TypeClassifier(),
                baseProgram with
                {
                    Finalizers = ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty
                        .Add(caller.DeclaringType, missingFinalizer),
                },
                new EmptyIntrinsicRegistry())).Message);

        var missingType = CliTypeIdentity.Named(
            ValidationTestData.Assembly,
            "Missing",
            "Type",
            false);
        Assert.Contains("finalizer", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.MethodInstances(),
                ValidationTestData.TypeClassifier(),
                baseProgram with
                {
                    Finalizers = ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty
                        .Add(missingType, caller),
                },
                new EmptyIntrinsicRegistry())).Message);

        var dispatch = new DispatchCallSiteModel("missing", 0, caller, []);
        Assert.Contains("canonical reachable caller", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.MethodInstances(),
                ValidationTestData.TypeClassifier(),
                baseProgram with
                {
                    DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                        .Add(dispatch.Key, dispatch),
                },
                new EmptyIntrinsicRegistry())).Message);

        var mismatchedDispatch = new DispatchCallSiteModel("caller", 0, caller, []);
        Assert.Contains("canonical reachable caller", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.MethodInstances(),
                ValidationTestData.TypeClassifier(),
                baseProgram with
                {
                    DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                        .Add("wrong-key", mismatchedDispatch),
                },
                new EmptyIntrinsicRegistry())).Message);

        var typeTest = new TypeTestSiteModel("missing", 0, caller.DeclaringType, []);
        Assert.Contains("canonical reachable caller", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.MethodInstances(),
                ValidationTestData.TypeClassifier(),
                baseProgram with
                {
                    TypeTestSites = ImmutableDictionary<string, TypeTestSiteModel>.Empty
                        .Add(typeTest.Key, typeTest),
                },
                new EmptyIntrinsicRegistry())).Message);

        var mismatchedTypeTest = new TypeTestSiteModel("caller", 0, caller.DeclaringType, []);
        Assert.Contains("canonical reachable caller", Assert.Throws<CompilerException>(() =>
            validator.Validate(
                ValidationTestData.MethodInstances(),
                ValidationTestData.TypeClassifier(),
                baseProgram with
                {
                    TypeTestSites = ImmutableDictionary<string, TypeTestSiteModel>.Empty
                        .Add("wrong-key", mismatchedTypeTest),
                },
                new EmptyIntrinsicRegistry())).Message);
    }

    // A delegate converted from another binds the other's Invoke, which has no body: its type's
    // invoke helper is its function (issue 223). A method named Invoke on another type, or a
    // delegate's other methods, still need a target.
    [Theory]
    [InlineData("Invoke", true, true)]
    [InlineData("Invoke", false, false)]
    [InlineData("Run", true, false)]
    public void OnlyADelegatesInvokeIsCallableWithoutATarget(string name, bool delegateType, bool valid)
    {
        var target = CreateTarget(name, 0x06000008);
        var program = ProgramWithInstructions(new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())) with
        {
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(target.CanonicalName, target),
        };
        var types = delegateType ? new DelegateTypeClassifier() : ValidationTestData.TypeClassifier();

        void Validate() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(), types, program, new EmptyIntrinsicRegistry());

        if (valid)
            Validate();
        else
            Assert.Contains("has no canonical reachable target", Assert.Throws<CompilerException>(Validate).Message);
    }

    // ldvirtftn may name a delegate's Invoke, whose dispatch target is its type's invoke helper
    // (issue 223); another method without a target still fails.
    [Theory]
    [InlineData("Invoke", true, true)]
    [InlineData("Invoke", false, false)]
    [InlineData("Run", true, false)]
    public void OnlyADelegatesInvokeIsADispatchTargetWithoutABody(string name, bool delegateType, bool valid)
    {
        var target = CreateTarget(name, 0x06000008);
        var program = ProgramWithInstructions(new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None()));
        var caller = program.MethodInstances.Values.Single().CanonicalName;
        program = program with
        {
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add($"{caller}@00000000",
                new DispatchCallSiteModel(caller, 0, target, [new DispatchTargetModel(target.DeclaringType, target)])),
        };
        var types = delegateType ? new DelegateTypeClassifier() : ValidationTestData.TypeClassifier();

        void Validate() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(), types, program, new EmptyIntrinsicRegistry());

        if (valid)
            Validate();
        else
            Assert.Contains("targets an unreachable method", Assert.Throws<CompilerException>(Validate).Message);
    }

    [Fact]
    public void RejectsHostCallbacksWithoutImportOrDelegateInvoke()
    {
        var program = ValidationTestData.Program();
        var invoke = CreateTarget("Invoke", 0x06000008);
        var callback = new HostCallbackDeclaration(
            new EntityKey(ValidationTestData.Assembly, 0x06000009),
            0,
            invoke,
            "callback");

        Assert.Contains("host callback", Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program with { HostCallbacks = [callback] },
            new EmptyIntrinsicRegistry())).Message);

        var import = new MethodDefinitionModel(
            callback.ImportMethod,
            invoke.Definition.DeclaringType,
            "Import",
            true,
            invoke.Signature,
            0);
        Assert.Contains("host callback", Assert.Throws<CompilerException>(() => CreateValidator().Validate(
            ValidationTestData.MethodInstances(),
            ValidationTestData.TypeClassifier(),
            program with
            {
                HostCallbacks = [callback],
                JSImportMethods = [import],
            },
            new EmptyIntrinsicRegistry())).Message);
    }

    private static ReachableProgram ProgramWithInstructions(params CilInstruction[] instructions)
    {
        var managed = ValidationTestData.ManagedMethodBody(instructions);
        var method = managed.Method.Definition;
        var instance = managed.Method;
        return new ReachableProgram(
            method,
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(method.Key, managed),
            [method.DeclaringType],
            [], [], [], [], [], [], [], [])
        {
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(instance.CanonicalName, instance),
        };
    }

    private static MethodInstanceModel CreateTarget(
        string name,
        int token,
        bool constructed = false)
    {
        var typeKey = new EntityKey(ValidationTestData.Assembly, 0x02000002);
        var definition = new MethodDefinitionModel(
            new EntityKey(ValidationTestData.Assembly, token),
            typeKey,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var declaringType = CliTypeIdentity.Named(
            ValidationTestData.Assembly,
            "",
            "Target",
            false);
        var arguments = constructed
            ? ImmutableArray.Create(CliTypeIdentity.FromStackKind(CliValueKind.I4))
            : ImmutableArray<CliTypeIdentity>.Empty;
        return new MethodInstanceModel(definition, declaringType, arguments, definition.Signature);
    }

    private sealed class DelegateTypeClassifier : ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => true;
    }

    private static ReachableProgram CreateUnitProgram()
    {
        var managed = CreateManagedMethodBody(
            new EntityKey(new AssemblyIdentity("ReachabilityUnitTest"), 0x06000001));
        var method = managed.Method.Definition;
        var instance = managed.Method;
        return new ReachableProgram(
            method,
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(method.Key, managed),
            [method.DeclaringType],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [])
        {
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(instance.CanonicalName, instance),
        };
    }

    private static IRuntimeIntrinsicRegistry CreateIntrinsics(
        ISymbolFormatter symbols,
        IEnumerable<MethodDefinitionModel> methods) =>
        new RuntimeIntrinsicRegistryFactory().Create(symbols, methods);

    private static ManagedMethodBody CreateManagedMethodBody(EntityKey methodKey)
    {
        var type = new EntityKey(methodKey.Assembly, 0x02000001);
        var method = new MethodDefinitionModel(
            methodKey,
            type,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(methodKey.Assembly, "Unit", "Fixture", false),
            [],
            method.Signature);
        return ValidationTestData.ManagedMethodBody(
            [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())],
            method,
            instance);
    }

    private sealed class EmptyIntrinsicRegistry : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return false;
        }
    }

    private sealed class SingleIntrinsicRegistry(EntityKey key) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            if (method == key)
            {
                intrinsic = RuntimeIntrinsic.StringLength;
                return true;
            }

            intrinsic = default;
            return false;
        }
    }

    private static IReachabilityInvariantValidator CreateValidator()
    {
        using var services = new ServiceCollection()
            .AddCompilerValidation()
            .BuildServiceProvider();
        return services.GetRequiredService<IReachabilityInvariantValidator>();
    }
}
