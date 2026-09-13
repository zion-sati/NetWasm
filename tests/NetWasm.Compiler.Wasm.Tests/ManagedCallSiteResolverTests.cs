using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedCallSiteResolverTests
{
    private readonly ManagedCallSiteResolver _resolver = new();

    [Fact]
    public void ResolveReturnsTheCanonicalReachabilityFact()
    {
        var caller = new ManagedMethodIdentity("Tests.Caller");
        InstructionEmissionRequest request = CreateRequest(caller, out ManagedCallSite expected);

        ManagedCallSite actual = ((IManagedCallSiteResolver)_resolver).Resolve(request);

        Assert.Same(expected, actual);
    }

    [Fact]
    public void ResolveRejectsANullRequest()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IManagedCallSiteResolver)_resolver).Resolve(null!));
    }

    [Fact]
    public void ResolveRejectsAMissingCallerIdentity()
    {
        InstructionEmissionRequest request = EmitterTestSupport.CreateInstructionRequest(CilOperation.Call);

        Assert.Throws<InvalidOperationException>(() =>
            ((IManagedCallSiteResolver)_resolver).Resolve(request));
    }

    [Fact]
    public void ResolveRejectsAMissingCanonicalCallSite()
    {
        var caller = new ManagedMethodIdentity("Tests.Caller");
        InstructionEmissionRequest request = EmitterTestSupport.CreateInstructionRequest(CilOperation.Call);
        request = request with
        {
            Context = request.Context with { CallerIdentity = caller },
            Target = request.Target with
            {
                ManagedCallSites = ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty,
            },
        };

        var exception = Assert.Throws<CompilerException>(() =>
            ((IManagedCallSiteResolver)_resolver).Resolve(request));
        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains(
            "MISSING_MANAGED_CALL_SITE",
            exception.Diagnostic.Message,
            StringComparison.Ordinal);
        Assert.Equal(caller.CanonicalName, exception.Diagnostic.Method);
        Assert.Equal(request.Instruction.Offset, exception.Diagnostic.IlOffset);
    }

    private static InstructionEmissionRequest CreateRequest(
        ManagedMethodIdentity caller,
        out ManagedCallSite callSite)
    {
        InstructionEmissionRequest request = EmitterTestSupport.CreateInstructionRequest(CilOperation.Call);
        MethodInstanceModel target = CreateMethod();
        var key = new ManagedCallSiteKey(caller, request.Instruction.Offset);
        callSite = new ManagedCallSite(
            key,
            ManagedCallOperation.Direct,
            new ManagedMethodIdentity(target.CanonicalName),
            target,
            ConstrainedType: null);

        return request with
        {
            Context = request.Context with { CallerIdentity = caller },
            Target = request.Target with
            {
                ManagedCallSites = ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty
                    .Add(key, callSite),
            },
        };
    }

    private static MethodInstanceModel CreateMethod()
    {
        var assembly = new AssemblyIdentity("Tests");
        var typeKey = new EntityKey(assembly, 100);
        var signature = new MethodSignatureModel(CliValueKind.Void, []);
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            typeKey,
            "Target",
            IsStatic: true,
            signature,
            RelativeVirtualAddress: 0);

        return new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(assembly, "Tests", "Container", isValueType: false),
            ImmutableArray<CliTypeIdentity>.Empty,
            signature);
    }
}
