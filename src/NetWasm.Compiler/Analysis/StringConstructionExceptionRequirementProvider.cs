using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class StringConstructionExceptionRequirementProvider :
    IStringConstructionExceptionRequirementProvider
{
    public ImmutableArray<ReachabilityExceptionRequirement> Discover(
        MethodDefinitionModel constructor)
    {
        var requirements = ImmutableArray.CreateBuilder<ReachabilityExceptionRequirement>();
        requirements.Add(new(
            ManagedExceptionKind.OutOfMemory,
            "System.OutOfMemoryException"));
        if (constructor.Signature.ParameterTypes.AsSpan().SequenceEqual(
            [CliValueKind.I4, CliValueKind.I4]))
        {
            requirements.Add(new(
                ManagedExceptionKind.ArgumentOutOfRange,
                "System.ArgumentOutOfRangeException"));
        }
        if (constructor.Signature.ParameterSignatureTypes.Length > 0 &&
            IsCharacterArray(constructor.Signature.ParameterSignatureTypes[0]))
        {
            requirements.Add(new(
                ManagedExceptionKind.ArgumentNull,
                "System.ArgumentNullException"));
            if (constructor.Signature.ParameterSignatureTypes.Length == 3)
            {
                requirements.Add(new(
                    ManagedExceptionKind.ArgumentOutOfRange,
                    "System.ArgumentOutOfRangeException"));
            }
        }
        return requirements.ToImmutable();
    }

    private static bool IsCharacterArray(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray &&
        type.ElementType!.CanonicalName == "primitive:char";
}
