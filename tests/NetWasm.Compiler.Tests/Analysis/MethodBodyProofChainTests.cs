using System.Collections.Generic;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

using static Constants.ConstantRewriteTestSupport;

public sealed class MethodBodyProofChainTests
{
    [Fact]
    public void RejectsMissingPasses()
    {
        var calls = new List<string>();
        Assert.Throws<ArgumentNullException>(() => new MethodBodyProofChain(null!, new Pass(calls, "repeated"), new Pass(calls, "objects"), new Pass(calls, "integers")));
        Assert.Throws<ArgumentNullException>(() => new MethodBodyProofChain(new Pass(calls, "bounds"), null!, new Pass(calls, "objects"), new Pass(calls, "integers")));
        Assert.Throws<ArgumentNullException>(() => new MethodBodyProofChain(new Pass(calls, "bounds"), new Pass(calls, "repeated"), null!, new Pass(calls, "integers")));
        Assert.Throws<ArgumentNullException>(() => new MethodBodyProofChain(new Pass(calls, "bounds"), new Pass(calls, "repeated"), new Pass(calls, "objects"), null!));
        var chain = new MethodBodyProofChain(new Pass(calls, "bounds"), new Pass(calls, "repeated"), new Pass(calls, "objects"), new Pass(calls, "integers"));
        Assert.Throws<ArgumentNullException>(() => chain.Prove(null!));
        Assert.Empty(calls);
    }

    // The bounds proof is planned first, as the repeated access and constructed object proofs add to it; each pass
    // takes the body the one before it returned.
    [Fact]
    public void PlansTheBoundsProofFirstAndHandsEachPassThePreviousBody()
    {
        var calls = new List<string>();
        var bounds = new Pass(calls, "bounds");
        var repeated = new Pass(calls, "repeated");
        var objects = new Pass(calls, "objects");
        var integers = new Pass(calls, "integers");
        var chain = new MethodBodyProofChain(bounds, repeated, objects, integers);
        var body = Body();

        var proved = chain.Prove(body);

        Assert.Equal(["bounds", "repeated", "objects", "integers"], calls);
        Assert.Same(body, bounds.Input);
        Assert.Same(bounds.Output, repeated.Input);
        Assert.Same(repeated.Output, objects.Input);
        Assert.Same(objects.Output, integers.Input);
        Assert.Same(integers.Output, proved);
    }

    private sealed class Pass(List<string> calls, string name) :
        IArrayBoundsCheckEliminator, IRepeatedArrayAccessEliminator, IConstructedObjectNullCheckEliminator, IIntegerCheckEliminator
    {
        public CilMethodBody? Input { get; private set; }
        public CilMethodBody? Output { get; private set; }

        public CilMethodBody Rewrite(CilMethodBody body)
        {
            calls.Add(name);
            Input = body;
            return Output = body with { MaxStack = body.MaxStack + 1 };
        }
    }
}
