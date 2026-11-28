// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Analysis.RuntimeAsync;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.RuntimeAsync;

using static NetWasm.Compiler.Tests.Analysis.StaticArrays.StaticArrayTestSupport;

// RuntimeAsync.ContextsMayChange loads false over a program in which nothing writes AmbientContextChanges.Made, and true
// over one that does; a program that never asks is left alone.
public sealed class ContextChangeFactProgramReducerTests
{
    private static readonly AssemblyIdentity CoreLib = new("CoreLibFacts");
    private static readonly EntityKey RuntimeKey = new(CoreLib, 0x02000001);
    private static readonly EntityKey ChangesKey = new(CoreLib, 0x02000002);
    private static readonly MethodDefinitionModel Query = new(new(CoreLib, 0x06000001), RuntimeKey, "ContextsMayChange", true,
        new MethodSignatureModel(CliTypeIdentity.Primitive("bool", CliValueKind.I4), []), 1);
    // Another method of that name, on a type that is not RuntimeAsync: not the query.
    private static readonly MethodDefinitionModel Namesake = Query with { Key = new(CoreLib, 0x06000002), DeclaringType = ChangesKey };
    private static readonly FieldDefinitionModel Made = new(new(CoreLib, 0x04000001), ChangesKey, "Made", CliValueKind.I4, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheQueryLoadsFalseOnlyWhereNothingWritesTheFlag(bool written)
    {
        var asking = Body([
            I(0, CilOperation.Call, Call(Query)),
            I(1, CilOperation.Call, Call(Namesake)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Return)]);
        var specializer = new CountingSpecializer();

        var changes = Reducer(written ? [Made.Key] : [], specializer).Reduce(Program(asking), ReachabilityRoots.Empty);

        var folded = Assert.Single(changes).Value;
        Assert.Equal([CilOperation.LoadInt32, CilOperation.Call, CilOperation.Pop, CilOperation.Pop, CilOperation.Return],
            folded.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(written ? 1 : 0), folded.Instructions[0].Operand);
        Assert.Equal(1, specializer.Calls);
    }

    [Fact]
    public void AProgramThatNeverAsksIsLeftAlone()
    {
        var quiet = Body([I(0, CilOperation.Call, Call(Namesake)), I(1, CilOperation.Pop), I(2, CilOperation.Return)]);
        var writes = new Writes([]);

        var changes = new ContextChangeFactProgramReducer(writes, new Types(), new Members(), new Members(), new CountingSpecializer())
            .Reduce(Program(quiet), ReachabilityRoots.Empty);

        Assert.Empty(changes);
        Assert.Equal(0, writes.Collections);
    }

    private static ContextChangeFactProgramReducer Reducer(ImmutableHashSet<EntityKey> written, IMethodSpecializer specializer) =>
        new(new Writes(written), new Types(), new Members(), new Members(), specializer);

    private static CilOperand.MethodInstance Call(MethodDefinitionModel method) => new(new MethodInstanceModel(method,
        CliTypeIdentity.Named(CoreLib, "", method.DeclaringType == RuntimeKey ? "RuntimeAsync" : "Other", false), [],
        method.Signature));

    private static ManagedMethodBody Body(CilInstruction[] instructions)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var definition = new MethodDefinitionModel(new(CoreLib, 0x06000010), RuntimeKey, "Asking", true, signature, 1);
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(CoreLib, "", "Asking", false), [], signature);
        return Managed(new CilMethodBody(definition, 2, [], [.. instructions]) { MethodInstance = method });
    }

    private sealed class Writes(ImmutableHashSet<EntityKey> written) : IStaticFieldWriteCollector
    {
        public int Collections { get; private set; }

        public ImmutableHashSet<EntityKey> Collect(ReachableProgram program, ReachabilityRoots roots)
        {
            Collections++;
            return written;
        }
    }

    private sealed class Types : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => fullName switch
        {
            "System.Runtime.CompilerServices.RuntimeAsync" =>
                new(RuntimeKey, "System.Runtime.CompilerServices", "RuntimeAsync", false, [], [Query.Key]),
            "System.Threading.AmbientContextChanges" =>
                new(ChangesKey, "System.Threading", "AmbientContextChanges", false, [Made.Key], [Namesake.Key]),
            _ => throw new KeyNotFoundException(fullName),
        };
    }

    private sealed class Members : IMethodRepository, IFieldRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => key == Query.Key ? Query : Namesake;

        public FieldDefinitionModel GetField(EntityKey key) => Made;
    }

    private sealed class CountingSpecializer : IMethodSpecializer
    {
        public int Calls { get; private set; }

        public CilMethodBody Rewrite(CilMethodBody body)
        {
            Calls++;
            return body;
        }
    }
}
