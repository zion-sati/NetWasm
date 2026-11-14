using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class CompilerDiagnosticProvenanceBinderTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PreservesExistingProvenanceAndAddsOnlyMissingDetails(bool hasSource, bool hasChain, bool emptyPath)
    {
        var source = new CompilerDiagnosticContext(new(new("Current"), 1), "Current.Method");
        var deeper = new CompilerDiagnosticContext(new(new("Deeper"), 2), "Deeper.Method");
        var root = new CompilerDependencyStep(source, CompilerDependencyKind.MethodDependency);
        var caller = new CompilerDependencyStep(deeper, CompilerDependencyKind.MethodDependency);
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, "Missing")
        {
            Context = hasSource ? deeper : null,
            DependencyChain = hasChain ? [caller] : default,
        };
        var failure = new CompilerException(diagnostic);
        var path = emptyPath ? ImmutableStack<CompilerDependencyStep>.Empty
            : ImmutableStack<CompilerDependencyStep>.Empty.Push(root).Push(caller);
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticProvenanceBinder>(new CompilerDiagnosticProvenanceBinder());

        var actual = binder.Bind(failure, source, path);

        Assert.Equal(hasSource ? deeper : source, actual.Diagnostic.Context);
        if (hasChain) Assert.Equal(caller, Assert.Single(actual.Diagnostic.DependencyChain));
        else if (emptyPath) Assert.True(actual.Diagnostic.DependencyChain.IsDefaultOrEmpty);
        else Assert.Equal(new[] { root, caller }, actual.Diagnostic.DependencyChain);
        if (hasSource && (hasChain || emptyPath)) Assert.Same(failure, actual);
        else Assert.Same(failure, actual.InnerException);
        Assert.Equal(diagnostic.Message, actual.Diagnostic.Message);
    }

    [Fact]
    public void RejectsNullInputs()
    {
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticProvenanceBinder>(new CompilerDiagnosticProvenanceBinder());
        var failure = new CompilerException(new(DiagnosticCode.AssemblyResolution, "Missing"));
        var source = new CompilerDiagnosticContext(new(new("Source"), 1));
        var path = ImmutableStack<CompilerDependencyStep>.Empty;
        Assert.Throws<ArgumentNullException>(() => binder.Bind(null!, source, path));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(failure, null!, path));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(failure, source, null!));
    }
}
