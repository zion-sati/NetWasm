using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Inlining;

public sealed class AccessorCanonicalizingMethodSpecializerTests
{
    [Fact]
    public void CanonicalizesAccessorsOfTheSpecializedBody()
    {
        var body = ValidationTestData.ManagedMethodBody().Body;
        var specialized = body with { MaxStack = body.MaxStack + 1 };
        var canonical = body with { MaxStack = body.MaxStack + 2 };
        var specializer = new Step(body, specialized);
        var accessors = new Step(specialized, canonical);

        Assert.Same(canonical, new AccessorCanonicalizingMethodSpecializer(specializer, accessors).Rewrite(body));
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var body = ValidationTestData.ManagedMethodBody().Body;
        Assert.Throws<ArgumentNullException>(() => new AccessorCanonicalizingMethodSpecializer(null!, new Step(body, body)));
        Assert.Throws<ArgumentNullException>(() => new AccessorCanonicalizingMethodSpecializer(new Step(body, body), null!));
    }

    private sealed class Step(CilMethodBody expected, CilMethodBody result) :
        IMethodSpecializer, ISimpleValueTypeAccessorCanonicalizer
    {
        public CilMethodBody Rewrite(CilMethodBody body) => Apply(body);

        public CilMethodBody Canonicalize(CilMethodBody body) => Apply(body);

        private CilMethodBody Apply(CilMethodBody body)
        {
            Assert.Same(expected, body);
            return result;
        }
    }
}
