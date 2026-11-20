// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests.Pipeline;

public sealed class PipelineReaderTests
{
    private static readonly AssemblyIdentity Assembly = new("Pipeline");
    private static readonly MethodDefinitionModel Method = new(new EntityKey(Assembly, 0x06000001),
        new EntityKey(Assembly, 0x02000001), "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1);

    // Where NW1016 is an error, a body that names a missing member fails as NW1016 by either kind of method; one that
    // names none is read as it is.
    [Fact]
    public void ABodyNamingAMissingMemberFailsByEitherKindOfMethod()
    {
        var missing = new MissingMemberReference(MissingMemberKind.Method, "Gone", "Run", 4, "Gone is missing");
        var instance = new MethodInstanceModel(Method, CliTypeIdentity.Named(Assembly, "Pipeline", "Holder", false), [],
            Method.Signature);
        var failing = new MissingMemberRejectingMethodBodyReader(new Bodies(new CilMethodBody(Method, 1, [], []) { MissingMember = missing }));
        var body = new CilMethodBody(Method, 1, [], []);
        var reading = new MissingMemberRejectingMethodBodyReader(new Bodies(body));

        Assert.Equal(DiagnosticCode.MissingApi, Assert.Throws<CompilerException>(() => failing.ReadMethodBody(Method)).Diagnostic.Code);
        Assert.Same(missing, Assert.Throws<CompilerException>(() => failing.ReadMethodBody(instance)).Diagnostic.MissingMember);
        Assert.Same(body, reading.ReadMethodBody(Method));
    }

    // Without native archives no call is variadic; with one, calls are planned from it.
    [Fact]
    public void VariadicCallsArePlannedOnlyFromNativeArchives()
    {
        var factory = new NativeVariadicCallPlannerFactory(new Archives());
        var options = new CompilerOptions("app.dll", [], "EntryPoint", "Run", []);

        Assert.Same(NoNativeVariadicCalls.Instance, factory.Create(options));
        Assert.IsType<NativeVariadicCallPlanner>(factory.Create(options with { NativeArchives = [("sqlite3", "libsqlite3.a")] }));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }

    private sealed class Bodies(CilMethodBody body) : IMethodBodyReader
    {
        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => body;

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => body;
    }

    private sealed class Archives : INativeArchiveFunctionTypeReader
    {
        public ImmutableDictionary<string, NativeFunctionType> Read(string archivePath) => ImmutableDictionary<string, NativeFunctionType>.Empty;
    }
}
