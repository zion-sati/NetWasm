using NetWasm.Compiler.Core;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;
using New = global::NetWasm.Compiler.ControlFlow.Structured;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class StructuredMethodCompleterTests
{
    [Fact]
    public void CompleteProjectsValidatesAndReturnsTheValidatedRepresentation()
    {
        var legacy = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var projected = ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                    new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(legacy);
        var calls = new List<string>();
        var drafts = new DraftAdapterProbe(projected, calls);
        var factory = new StructuredMethodFactoryProbe(calls);
        var completer = new New.StructuredMethodCompleter(drafts, factory);

        var result = ((New.IStructuredMethodCompleter)completer).Complete(legacy);

        Assert.Same(projected, factory.Construction);
        Assert.Same(factory.Result, result);
        Assert.Equal(["adapt", "validate"], calls);
    }

    [Fact]
    public void CompleteRequiresItsCapabilitiesAndDraft()
    {
        var calls = new List<string>();
        var method = ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                    new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(
                DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return))));
        var drafts = new DraftAdapterProbe(method, calls);
        var factory = new StructuredMethodFactoryProbe(calls);

        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodCompleter(null!, factory));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodCompleter(drafts, null!));
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredMethodCompleter)new New.StructuredMethodCompleter(drafts, factory)).Complete(null!));
    }

    private sealed class DraftAdapterProbe(New.StructuredMethodConstruction result, List<string> calls) :
        New.IStructuredMethodDraftAdapter
    {
        public New.StructuredMethodConstruction Adapt(NwDraft.StructuredMethodDraft draft)
        {
            calls.Add("adapt");
            return result;
        }
    }

    private sealed class StructuredMethodFactoryProbe(List<string> calls) : New.IStructuredMethodFactory
    {
        public New.StructuredMethodConstruction? Construction { get; private set; }

        public New.StructuredMethod? Result { get; private set; }

        public New.StructuredMethod Create(New.StructuredMethodConstruction construction)
        {
            calls.Add("validate");
            Construction = construction;
            return Result = new(
                construction.Header,
                construction.EntryBlock,
                construction.Blocks,
                construction.Body,
                construction.TopLevelExceptionGroups,
                construction.ExceptionGroups,
                construction.InstructionEntryStacks);
        }
    }
}
