using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class CompilerDiagnosticDependencyPathBinderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinsOnlyTheExactClosedIdentityAndPreservesTheFailure(bool structuralChain)
    {
        var type = new CompilerDiagnosticContext(new(new("Library"), 1), "Library.Box<int>");
        var structural = new CompilerDependencyStep(type, CompilerDependencyKind.Interface);
        var caller = new CompilerDependencyStep(new(new(new("App"), 2), "App.Run"), CompilerDependencyKind.MethodDependency);
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, "Missing")
        {
            Context = type,
            DependencyChain = structuralChain ? [structural] : [],
        };
        var paths = new Dictionary<string, ImmutableStack<CompilerDependencyStep>>
        {
            [type.InstanceIdentity!] = ImmutableStack<CompilerDependencyStep>.Empty.Push(caller),
        };
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticDependencyPathBinder>(new CompilerDiagnosticDependencyPathBinder());

        var result = binder.Bind(diagnostic, paths);

        Assert.Equal(caller, result.DependencyChain[0]);
        Assert.Equal(structuralChain ? 2 : 1, result.DependencyChain.Length);
        if (structuralChain) Assert.Equal(structural, result.DependencyChain[1]);
        Assert.Same(diagnostic.Context, result.Context);
        Assert.Equal(diagnostic.Message, result.Message);
        Assert.Same(diagnostic, binder.Bind(diagnostic, new Dictionary<string, ImmutableStack<CompilerDependencyStep>>
        {
            ["Library.Box<string>"] = paths[type.InstanceIdentity!],
        }));
        paths[type.InstanceIdentity!] = ImmutableStack<CompilerDependencyStep>.Empty;
        Assert.Same(diagnostic, binder.Bind(diagnostic, paths));

    }

    // A diagnostic whose chain was cleared to default gets the path alone.
    [Fact]
    public void ADefaultChainBecomesThePath()
    {
        var type = new CompilerDiagnosticContext(new(new("Library"), 1), "Library.Box<int>");
        var caller = new CompilerDependencyStep(new(new(new("App"), 2), "App.Run"), CompilerDependencyKind.MethodDependency);
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, "Missing") { Context = type, DependencyChain = default };
        var paths = new Dictionary<string, ImmutableStack<CompilerDependencyStep>>
        {
            [type.InstanceIdentity!] = ImmutableStack<CompilerDependencyStep>.Empty.Push(caller),
        };

        var result = new CompilerDiagnosticDependencyPathBinder().Bind(diagnostic, paths);

        Assert.Equal([caller], result.DependencyChain);
    }

    [Fact]
    public void FieldWithoutOwnerCannotBorrowItsValueTypesCaller()
    {
        var source = new CompilerDiagnosticContext(new(new("Library"), 0x04000001), "Shared.Value");
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, "Missing") { Context = source };
        var caller = new CompilerDependencyStep(new(new(new("App"), 1)), CompilerDependencyKind.MethodDependency);
        var paths = new Dictionary<string, ImmutableStack<CompilerDependencyStep>>
        {
            [source.InstanceIdentity!] = ImmutableStack<CompilerDependencyStep>.Empty.Push(caller),
        };
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticDependencyPathBinder>(new CompilerDiagnosticDependencyPathBinder());
        Assert.Same(diagnostic, binder.Bind(diagnostic, paths));
    }

    [Fact]
    public void FieldLookupUsesItsClosedOwnerRatherThanItsSharedValueType()
    {
        var box = CliTypeIdentity.Named(new("Library"), "Tests", "Box`1", false);
        var owner = CliTypeIdentity.GenericInstantiation(box, [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var otherOwner = CliTypeIdentity.GenericInstantiation(box, [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);
        var context = new CompilerDiagnosticContext(new(new("Library"), 2), "Shared.Value") { DeclaringType = owner };
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, "Missing") { Context = context };
        var caller = new CompilerDependencyStep(new(new(new("App"), 1)), CompilerDependencyKind.MethodDependency);
        var paths = new Dictionary<string, ImmutableStack<CompilerDependencyStep>>
        {
            [otherOwner.CanonicalName] = ImmutableStack<CompilerDependencyStep>.Empty.Push(caller),
            [context.InstanceIdentity!] = ImmutableStack<CompilerDependencyStep>.Empty.Push(caller),
        };
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticDependencyPathBinder>(new CompilerDiagnosticDependencyPathBinder());
        Assert.Same(diagnostic, binder.Bind(diagnostic, paths));
        paths.Add(owner.CanonicalName, ImmutableStack<CompilerDependencyStep>.Empty.Push(caller));
        Assert.Equal(caller, Assert.Single(binder.Bind(diagnostic, paths).DependencyChain));
    }

    [Fact]
    public void MissingContextAndInvalidInputsAreHandledWithoutInventingAPath()
    {
        var binder = Assert.IsAssignableFrom<ICompilerDiagnosticDependencyPathBinder>(new CompilerDiagnosticDependencyPathBinder());
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, "Missing");
        var paths = new Dictionary<string, ImmutableStack<CompilerDependencyStep>>();
        Assert.Same(diagnostic, binder.Bind(diagnostic, paths));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(null!, paths));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(diagnostic, null!));
    }
}
