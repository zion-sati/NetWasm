using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class FieldLayoutFailureBinderTests
{
    [Fact]
    public void RecordsContainingFieldsInOrderWithoutReplacingTheInnermostSource()
    {
        var binder = Assert.IsAssignableFrom<IFieldLayoutFailureBinder>(new FieldLayoutFailureBinder());
        var inner = new CompilerDiagnosticContext(new(new("Library"), 0x04000001), "Inner.Value");
        var outer = new CompilerDiagnosticContext(new(new("Library"), 0x04000002), "Outer.Value");
        var original = new CompilerException(new(DiagnosticCode.AssemblyResolution, "Missing field type")
        {
            DependencyChain = default,
        });
        var first = binder.Bind(original, inner);
        var result = binder.Bind(first, outer);
        Assert.Same(inner, result.Diagnostic.Context);
        Assert.Same(first, result.InnerException);
        Assert.Same(original, first.InnerException);
        Assert.Equal(original.Diagnostic.Message, result.Diagnostic.Message);
        Assert.Collection(result.Diagnostic.DependencyChain,
            step => Assert.Equal(new(outer, CompilerDependencyKind.FieldStorage), step),
            step => Assert.Equal(new(inner, CompilerDependencyKind.FieldStorage), step));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(null!, inner));
        Assert.Throws<ArgumentNullException>(() => binder.Bind(original, null!));
    }
}
