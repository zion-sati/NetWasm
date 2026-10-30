using System;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

// A callee examined for inlining reads its value-type fields directly, as its
// caller will once both are optimized, rather than through accessor calls.
internal sealed class AccessorCanonicalizingMethodSpecializer(
    IMethodSpecializer specializer,
    ISimpleValueTypeAccessorCanonicalizer accessors) : IMethodSpecializer
{
    private readonly IMethodSpecializer _specializer = specializer ??
        throw new ArgumentNullException(nameof(specializer));
    private readonly ISimpleValueTypeAccessorCanonicalizer _accessors = accessors ??
        throw new ArgumentNullException(nameof(accessors));

    public CilMethodBody Rewrite(CilMethodBody body) => _accessors.Canonicalize(_specializer.Rewrite(body));
}
