using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Validation.Delegates;

namespace NetWasm.Compiler.Tests.Validation.Delegates;

public sealed class DelegateBindingInvariantValidatorTests
{
    private static readonly AssemblyIdentity Assembly = new("binding-invariants");
    private static readonly EntityKey DelegateTypeKey = new(Assembly, 1);
    private static readonly EntityKey TargetTypeKey = new(Assembly, 2);
    private static readonly CliTypeIdentity ReferenceType = CliTypeIdentity.Named(
        Assembly,
        "BindingInvariants",
        "Reference",
        isValueType: false);
    private static readonly CliTypeIdentity BaseReferenceType = CliTypeIdentity.Named(
        Assembly,
        "BindingInvariants",
        "BaseReference",
        isValueType: false);

    [Fact]
    public void ValidatorAcceptsCanonicalBindingThroughItsContract()
    {
        var binding = ValidBinding();
        var program = Program(binding);
        var validator = Validator();

        validator.Validate(new DelegateTypeClassifier(), program);
    }

    [Fact]
    public void ValidatorRejectsMissingInputsAndNonCanonicalIdentities()
    {
        var binding = ValidBinding();
        var program = Program(binding);
        var validator = Validator();
        var badInvoke = binding with
        {
            InvokeIdentity = new ManagedMethodIdentity("wrong-invoke"),
        };
        var badTarget = binding with
        {
            TargetIdentity = new ManagedMethodIdentity("wrong-target"),
        };

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, program));
        Assert.Throws<ArgumentNullException>(() =>
            validator.Validate(new DelegateTypeClassifier(), null!));
        AssertInvariant(validator, Program(badInvoke));
        AssertInvariant(validator, Program(badTarget));
    }

    [Fact]
    public void ValidatorRejectsNonInvokeAndNonDelegateBindings()
    {
        var binding = ValidBinding();
        var validator = Validator();
        var wrongNameInvoke = Method(
            "Other",
            DelegateTypeKey,
            3,
            ReferenceType,
            ReferenceType);
        var wrongName = RebindInvoke(binding, wrongNameInvoke);

        AssertInvariant(validator, Program(wrongName));
        AssertInvariant(
            validator,
            Program(binding),
            new NonDelegateTypeClassifier());
    }

    [Fact]
    public void ValidatorRejectsMissingAndMismatchedCallableTargets()
    {
        var binding = ValidBinding();
        var validator = Validator();
        var missing = Program(binding) with
        {
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty,
        };
        var replacement = Method(
            "Replacement",
            TargetTypeKey,
            4,
            ReferenceType,
            ReferenceType);
        var mismatched = Program(binding) with
        {
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                binding.TargetIdentity.CanonicalName,
                replacement),
        };

        AssertInvariant(validator, missing);
        AssertInvariant(validator, mismatched);
    }

    // A static closed over its first argument binds the Invoke's parameters to the rest; only a
    // static whose first parameter is a reference can close over it (issue 223's class).
    [Fact]
    public void ValidatorChecksAStaticClosedOverItsFirstArgument()
    {
        var binding = ValidBinding();
        var validator = Validator();
        var closedTarget = Method(binding.Target.Definition.Name, TargetTypeKey,
            binding.Target.Definition.Key.MetadataToken, ReferenceType, ReferenceType, BaseReferenceType);
        var closed = RebindTarget(binding, closedTarget) with { ClosedOverFirstArgument = true };
        var scalarFirst = RebindTarget(closed, Method(binding.Target.Definition.Name, TargetTypeKey,
            binding.Target.Definition.Key.MetadataToken, ReferenceType, CliTypeIdentity.FromStackKind(CliValueKind.I4),
            BaseReferenceType));
        var instance = RebindTarget(closed, closedTarget with
        {
            Definition = closedTarget.Definition with { IsStatic = false },
        });
        var noParameters = RebindTarget(closed, Method(binding.Target.Definition.Name, TargetTypeKey,
            binding.Target.Definition.Key.MetadataToken, ReferenceType));

        validator.Validate(new DelegateTypeClassifier(), Program(closed));
        AssertInvariant(validator, Program(scalarFirst));
        AssertInvariant(validator, Program(instance));
        AssertInvariant(validator, Program(noParameters));
        AssertInvariant(validator, Program(binding with { ClosedOverFirstArgument = true }));
    }

    [Fact]
    public void ValidatorRejectsParameterCountAndIdentityMismatches()
    {
        var binding = ValidBinding();
        var validator = Validator();
        var missingBinding = binding with { ParameterBindings = [] };
        var extraTargetParameter = Method(
            binding.Target.Definition.Name,
            TargetTypeKey,
            binding.Target.Definition.Key.MetadataToken,
            ReferenceType,
            ReferenceType,
            ReferenceType);
        var wrongTargetCount = RebindTarget(binding, extraTargetParameter);
        var wrongSource = binding with
        {
            ParameterBindings =
            [
                binding.ParameterBindings[0] with
                {
                    SourceType = Type("WrongSource"),
                },
            ],
        };
        var wrongTarget = binding with
        {
            ParameterBindings =
            [
                binding.ParameterBindings[0] with
                {
                    TargetType = Type("WrongTarget"),
                },
            ],
        };

        AssertInvariant(validator, Program(missingBinding));
        AssertInvariant(validator, Program(wrongTargetCount));
        AssertInvariant(validator, Program(wrongSource));
        AssertInvariant(validator, Program(wrongTarget));
    }

    [Fact]
    public void ValidatorRejectsInvalidParameterAdaptationShapes()
    {
        var binding = ValidBinding();
        var validator = Validator();
        var nonIdentity = binding with
        {
            ParameterBindings =
            [
                binding.ParameterBindings[0] with
                {
                    Adaptation = ManagedDelegateAdaptation.Identity,
                },
            ],
        };
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var scalarInvoke = Method(
            "Invoke",
            DelegateTypeKey,
            binding.Invoke.Definition.Key.MetadataToken,
            ReferenceType,
            scalar);
        var nonReferenceSource = RebindInvoke(binding, scalarInvoke) with
        {
            ParameterBindings =
            [
                new ManagedDelegateValueBinding(
                    scalar,
                    binding.ParameterBindings[0].TargetType,
                    ManagedDelegateAdaptation.ReferenceConversion),
            ],
        };
        var scalarTarget = Method(
            "Target",
            TargetTypeKey,
            binding.Target.Definition.Key.MetadataToken,
            ReferenceType,
            scalar);
        var nonReferenceTarget = RebindTarget(binding, scalarTarget) with
        {
            ParameterBindings =
            [
                new ManagedDelegateValueBinding(
                    binding.ParameterBindings[0].SourceType,
                    scalar,
                    ManagedDelegateAdaptation.ReferenceConversion),
            ],
        };
        var unknown = binding with
        {
            ParameterBindings =
            [
                binding.ParameterBindings[0] with
                {
                    Adaptation = (ManagedDelegateAdaptation)99,
                },
            ],
        };

        AssertInvariant(validator, Program(nonIdentity));
        AssertInvariant(validator, Program(nonReferenceSource));
        AssertInvariant(validator, Program(nonReferenceTarget));
        AssertInvariant(validator, Program(unknown));
    }

    [Fact]
    public void ValidatorRejectsReturnIdentityAndAdaptationMismatches()
    {
        var binding = ValidBinding();
        var validator = Validator();
        var wrongSource = binding with
        {
            ReturnBinding = binding.ReturnBinding with
            {
                SourceType = Type("WrongSource"),
            },
        };
        var wrongTarget = binding with
        {
            ReturnBinding = binding.ReturnBinding with
            {
                TargetType = Type("WrongTarget"),
            },
        };
        var wrongShape = binding with
        {
            ReturnBinding = binding.ReturnBinding with
            {
                Adaptation = ManagedDelegateAdaptation.ReferenceConversion,
            },
        };

        AssertInvariant(validator, Program(wrongSource));
        AssertInvariant(validator, Program(wrongTarget));
        AssertInvariant(validator, Program(wrongShape));
    }

    [Fact]
    public void ValidatorRejectsMissingFactoryDependency()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateBindingInvariantValidator(null!));
    }

    private static IDelegateBindingInvariantValidator Validator() => new[]
    {
        new DelegateBindingInvariantValidator(
            new CompilerInvariantExceptionFactory()),
    }.Cast<IDelegateBindingInvariantValidator>().Single();

    private static void AssertInvariant(
        IDelegateBindingInvariantValidator validator,
        ReachableProgram program,
        ITypeClassifier? classifier = null) =>
        Assert.Throws<CompilerException>(() => validator.Validate(
            classifier ?? new DelegateTypeClassifier(),
            program));

    private static ManagedDelegateBinding ValidBinding()
    {
        var invoke = Method(
            "Invoke",
            DelegateTypeKey,
            1,
            ReferenceType,
            ReferenceType);
        var target = Method(
            "Target",
            TargetTypeKey,
            2,
            ReferenceType,
            BaseReferenceType);
        return new(
            new ManagedMethodIdentity(invoke.CanonicalName),
            new ManagedMethodIdentity(target.CanonicalName),
            invoke,
            target,
            [
                new ManagedDelegateValueBinding(
                    ReferenceType,
                    BaseReferenceType,
                    ManagedDelegateAdaptation.ReferenceConversion),
            ],
            new ManagedDelegateValueBinding(
                ReferenceType,
                ReferenceType,
                ManagedDelegateAdaptation.Identity));
    }

    private static ManagedDelegateBinding RebindInvoke(
        ManagedDelegateBinding binding,
        MethodInstanceModel invoke) => binding with
        {
            Invoke = invoke,
            InvokeIdentity = new ManagedMethodIdentity(invoke.CanonicalName),
        };

    private static ManagedDelegateBinding RebindTarget(
        ManagedDelegateBinding binding,
        MethodInstanceModel target) => binding with
        {
            Target = target,
            TargetIdentity = new ManagedMethodIdentity(target.CanonicalName),
        };

    private static ReachableProgram Program(ManagedDelegateBinding binding)
    {
        var entry = Method(
            "Run",
            TargetTypeKey,
            10,
            CliTypeIdentity.FromStackKind(CliValueKind.Void));
        return new ReachableProgram(
            entry.Definition,
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [])
        {
            DelegateBindings = [binding],
            CallableMethods =
                ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    binding.TargetIdentity.CanonicalName,
                    binding.Target),
        };
    }

    private static MethodInstanceModel Method(
        string name,
        EntityKey declaringTypeKey,
        int token,
        CliTypeIdentity returnType,
        params CliTypeIdentity[] parameters)
    {
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000000 + token),
            declaringTypeKey,
            name,
            true,
            new MethodSignatureModel(returnType, [.. parameters]),
            1);
        return new(
            definition,
            CliTypeIdentity.Named(
                Assembly,
                "BindingInvariants",
                declaringTypeKey == DelegateTypeKey ? "Callback" : "Target",
                isValueType: false),
            [],
            definition.Signature);
    }

    private static CliTypeIdentity Type(string name) => CliTypeIdentity.Named(
        Assembly,
        "BindingInvariants",
        name,
        isValueType: false);

    private sealed class DelegateTypeClassifier : ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => type == DelegateTypeKey;
    }

    private sealed class NonDelegateTypeClassifier : ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => false;
    }
}
