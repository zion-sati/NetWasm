using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Validation.Delegates;

internal sealed class DelegateBindingInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions) :
    IDelegateBindingInvariantValidator
{
    private readonly ICompilerInvariantExceptionFactory _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));

    public void Validate(ITypeClassifier types, ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(program);

        foreach (var binding in program.DelegateBindings)
        {
            if (binding.InvokeIdentity.CanonicalName != binding.Invoke.CanonicalName ||
                binding.TargetIdentity.CanonicalName != binding.Target.CanonicalName)
            {
                throw _exceptions.Create(
                    "delegate binding has a non-canonical method identity");
            }

            if (binding.Invoke.Definition.Name != "Invoke" ||
                !types.IsDelegateType(binding.Invoke.Definition.DeclaringType))
            {
                throw _exceptions.Create(
                    "delegate binding does not target a delegate Invoke method");
            }

            if (!program.CallableMethods.TryGetValue(
                    binding.TargetIdentity.CanonicalName,
                    out var callable) ||
                callable.CanonicalName != binding.Target.CanonicalName)
            {
                throw _exceptions.Create(
                    "delegate binding target is absent from the callable method set");
            }

            ValidateSignature(binding);
        }
    }

    private void ValidateSignature(ManagedDelegateBinding binding)
    {
        var invokeParameters = binding.Invoke.Signature.ParameterSignatureTypes;
        var targetParameters = binding.Target.Signature.ParameterSignatureTypes;
        if (binding.ParameterBindings.Length != invokeParameters.Length ||
            targetParameters.Length != invokeParameters.Length)
        {
            throw _exceptions.Create(
                "delegate binding parameter count does not match its methods");
        }

        for (var index = 0; index < binding.ParameterBindings.Length; index++)
        {
            var parameter = binding.ParameterBindings[index];
            if (!parameter.SourceType.Equals(invokeParameters[index]) ||
                !parameter.TargetType.Equals(targetParameters[index]) ||
                !HasValidShape(parameter))
            {
                throw _exceptions.Create(
                    "delegate binding parameter adaptation is inconsistent");
            }
        }

        var returnBinding = binding.ReturnBinding;
        if (!returnBinding.SourceType.Equals(
                binding.Target.Signature.ReturnSignatureType) ||
            !returnBinding.TargetType.Equals(
                binding.Invoke.Signature.ReturnSignatureType) ||
            !HasValidShape(returnBinding))
        {
            throw _exceptions.Create(
                "delegate binding return adaptation is inconsistent");
        }
    }

    private static bool HasValidShape(ManagedDelegateValueBinding binding) =>
        binding.Adaptation switch
        {
            ManagedDelegateAdaptation.Identity =>
                binding.SourceType.Equals(binding.TargetType),
            ManagedDelegateAdaptation.ReferenceConversion =>
                !binding.SourceType.Equals(binding.TargetType) &&
                binding.SourceType.StackKind == CliValueKind.ManagedReference &&
                binding.TargetType.StackKind == CliValueKind.ManagedReference,
            _ => false,
        };
}
