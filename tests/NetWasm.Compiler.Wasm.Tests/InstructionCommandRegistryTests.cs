using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class InstructionCommandRegistryTests
{
    [Fact]
    public void FamilyCatalogOwnsEverySupportedOperationExactlyOnce()
    {
        var commands = CreateInstructionCommands(_ => { });
        var registry = new InstructionCommandRegistry(commands);
        var families = InstructionFamilyCatalogFactory.CreateDefault();
        Func<IInstructionFamilyResolver, CilOperation, InstructionFamily> resolve =
            static (resolver, operation) => resolver.Resolve(operation);

        foreach (var operation in SupportedCil.Operations)
        {
            Assert.Equal(
                resolve(families, operation),
                registry.Resolve(operation).Family);
        }
        Assert.Equal(
            SupportedCil.Operations.Length,
            commands.Length);
    }

    [Fact]
    public void DuplicateOwnershipFailsBeforeEmission()
    {
        var first = Handler(CilOperation.Nop);
        var second = Handler(CilOperation.Nop);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InstructionCommandRegistry(
                [first, second],
                [CilOperation.Nop]));

        Assert.Equal(
            "CIL operation 'Nop' has more than one instruction command.",
            exception.Message);
    }

    [Fact]
    public void MissingOwnershipFailsBeforeEmission()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InstructionCommandRegistry([], [CilOperation.LoadInt32]));

        Assert.Equal(
            "CIL operation 'LoadInt32' has no instruction command.",
            exception.Message);
    }

    [Fact]
    public void UnregisteredOperationProducesCompilerDiagnostic()
    {
        var registry = new InstructionCommandRegistry(
            [Handler(CilOperation.Nop)],
            [CilOperation.Nop]);

        var exception = Assert.Throws<CompilerException>(() =>
            registry.Resolve(CilOperation.LoadInt32));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("LoadInt32", exception.Message);
    }

    private static InstructionCommand Handler(CilOperation operation) =>
        new(
            operation,
            InstructionFamily.ConstantsStackLocalsArguments,
            (_, _) => { });
}
