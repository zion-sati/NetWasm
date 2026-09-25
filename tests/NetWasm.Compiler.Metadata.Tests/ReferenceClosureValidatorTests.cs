using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ReferenceClosureValidatorTests
{
    [Fact]
    public void ValidateAcceptsResolvedDirectAndAliasedReferences()
    {
        var validator = new ReferenceClosureValidator();
        var validate = ((IReferenceClosureValidator)validator).Validate;

        validate(
            [Closure("Application", "Library", "System.Runtime")],
            ["Application", "Library", "Core"],
            ImmutableDictionary<string, string>.Empty.Add("System.Runtime", "Core"));
    }

    [Fact]
    public void ValidateRejectsForbiddenAndUnresolvedReferences()
    {
        var validator = new ReferenceClosureValidator();
        var validate = ((IReferenceClosureValidator)validator).Validate;

        var forbidden = Assert.Throws<CompilerException>(() => validate(
            [Closure("Application", "System.Runtime")],
            ["Application", "System.Runtime"],
            ImmutableDictionary<string, string>.Empty));
        Assert.Equal(DiagnosticCode.AssemblyResolution, forbidden.Diagnostic.Code);
        Assert.Contains("forbidden desktop assembly", forbidden.Message);

        var unresolved = Assert.Throws<CompilerException>(() => validate(
            [Closure("Application", "Missing")],
            ["Application"],
            ImmutableDictionary<string, string>.Empty));
        Assert.Equal(DiagnosticCode.AssemblyResolution, unresolved.Diagnostic.Code);
        Assert.Contains("unresolved reference", unresolved.Message);
    }

    private static AssemblyReferenceClosure Closure(
        string assembly,
        params string[] references) => new(new(assembly), [.. references]);
}
