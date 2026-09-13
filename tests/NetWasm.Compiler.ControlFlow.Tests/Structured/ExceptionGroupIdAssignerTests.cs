using System.Collections.Immutable;

using DraftTests = global::NetWasm.Compiler.ControlFlow.Tests.Draft;

using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class ExceptionGroupIdAssignerTests
{
    [Fact]
    public void AssignUsesInputOrderAndReferenceIdentity()
    {
        var assigner = Assert.IsAssignableFrom<IExceptionGroupIdAssigner>(
            new ExceptionGroupIdAssigner());
        var first = DraftTests.ExceptionGroupCollectorTests.Group();
        var equalButDistinct = first with { };

        var assignments = assigner.Assign([first, equalButDistinct]);

        Assert.Equal(first, equalButDistinct);
        Assert.Equal(0, assignments[first].Value);
        Assert.Equal(1, assignments[equalButDistinct].Value);
    }

    [Fact]
    public void AssignReturnsAnEmptyMapForNoGroups()
    {
        var assigner = Assert.IsAssignableFrom<IExceptionGroupIdAssigner>(
            new ExceptionGroupIdAssigner());

        Assert.Empty(assigner.Assign([]));
    }

    [Fact]
    public void AssignRequiresAnInitializedGroupCollection()
    {
        var assigner = Assert.IsAssignableFrom<IExceptionGroupIdAssigner>(
            new ExceptionGroupIdAssigner());

        Assert.Throws<ArgumentException>(
            () => assigner.Assign(default(ImmutableArray<global::NetWasm.Compiler.ControlFlow.Draft.StructuredExceptionGroupDraft>)));
    }

    [Fact]
    public void AssignRejectsTheSameGroupReferenceTwice()
    {
        var assigner = Assert.IsAssignableFrom<IExceptionGroupIdAssigner>(
            new ExceptionGroupIdAssigner());
        var group = DraftTests.ExceptionGroupCollectorTests.Group();

        Assert.Throws<ArgumentException>(() => assigner.Assign([group, group]));
    }
}
