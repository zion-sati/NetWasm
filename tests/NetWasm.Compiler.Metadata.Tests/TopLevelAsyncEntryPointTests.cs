// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class TopLevelAsyncEntryPointTests
{
    [Fact]
    public void RejectsMissingInstructionReader() =>
        Assert.Throws<ArgumentNullException>(() => new ManagedExecutableEntryPointSelector(null!));

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void SelectsTheAsyncMethodCalledByTheGeneratedWrapper(bool returnsCode, bool release, bool decoy)
    {
        var resultType = returnsCode ? "Task<int>" : "Task";
        var result = returnsCode ? "Task.FromResult(0)" : "Task.CompletedTask";
        var source = "using System.Threading.Tasks; await Task.Yield();" +
            (returnsCode ? " return 42;" : "") +
            (decoy ? $" partial class Program {{ static {resultType} Main(string[] args) => {result}; }}" : "");
        var image = Compile(source, release);
        using var pe = new PEReader(new MemoryStream(image));
        var wrapper = pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress;
        var selector = Assert.IsAssignableFrom<IManagedExecutableEntryPointSelector>(new ManagedExecutableEntryPointSelectorFactory().Create());
        var selected = selector.SelectEntryPoint("virtual/top-level.dll", image);
        Assert.Equal("<Main>$", selected.MethodName);
        Assert.NotEqual(wrapper, selected.MetadataToken);
        Assert.Equal(new ManagedExecutableEntryPointAbi(ManagedExecutableParameterShape.StringArray,
            returnsCode ? ManagedExecutableReturnShape.ExitCode : ManagedExecutableReturnShape.Void,
            ManagedExecutableCompletionShape.Asynchronous), selected.Abi);
        Assert.Equal(selected, selector.SelectEntryPoint("virtual/top-level.dll", image, selected.MetadataToken));
        Assert.Equal(ManagedExecutableCompletionShape.Synchronous,
            selector.SelectEntryPoint("virtual/top-level.dll", image, wrapper).Abi.CompletionShape);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronousTopLevelEntryRemainsSynchronous(bool release)
    {
        var selected = new ManagedExecutableEntryPointSelectorFactory().Create().SelectEntryPoint("virtual/sync.dll", Compile("return 42;", release));
        Assert.Equal("<Main>$", selected.MethodName);
        Assert.Equal(ManagedExecutableCompletionShape.Synchronous, selected.Abi.CompletionShape);
        Assert.Equal(ManagedExecutableReturnShape.ExitCode, selected.Abi.ReturnShape);
    }

    [Fact]
    public void IgnoresWrapperCallsThatAreNotAsyncEntryCandidates()
    {
        // Roslyn's wrapper calls only the real entry, so the reader reports a call
        // to every method: each decoy fails one candidate check.
        var image = Compile("""
            using System.Threading.Tasks;
            await Task.Yield();
            partial class Program
            {
                Task Main(int value) => Task.CompletedTask;
                static Task Main<T>() => Task.CompletedTask;
                static Task Main() => Task.CompletedTask;
                static Task Main(int first, int second) => Task.CompletedTask;
                static string Main(object value) => "";
                static Program Main(long value) => null;
                static ValueTask Main(short value) => default;
                static Task<long> Main(byte value) => Task.FromResult(0L);
                static Task Helper() => Task.CompletedTask;
            }
            static class Other
            {
                public static Task Main() => Task.CompletedTask;
            }
            """, release: false);
        using var pe = new PEReader(new MemoryStream(image));
        var methods = pe.GetMetadataReader().MethodDefinitions.Count;

        var selected = new ManagedExecutableEntryPointSelector(new EveryMethodCallReader(methods))
            .SelectEntryPoint("virtual/decoys.dll", image);

        Assert.Equal("<Main>$", selected.MethodName);
        Assert.Equal(new ManagedExecutableEntryPointAbi(ManagedExecutableParameterShape.StringArray,
            ManagedExecutableReturnShape.Void, ManagedExecutableCompletionShape.Asynchronous), selected.Abi);
    }

    private sealed class EveryMethodCallReader(int methods) : IRawCilInstructionReader
    {
        // A non-call and a call to a member reference are ignored too.
        public ImmutableArray<RawCilInstruction> Read(ReadOnlySpan<byte> bytes) =>
        [
            new(0, OpCodes.Nop, null),
            new(1, OpCodes.Call, 0x0A000001),
            .. Enumerable.Range(1, methods).Select(row => new RawCilInstruction(0, OpCodes.Call, 0x06000000 | row)),
        ];
    }

    private static byte[] Compile(string source, bool release)
    {
        var compilation = CSharpCompilation.Create("EntryFixture", [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                optimizationLevel: release ? OptimizationLevel.Release : OptimizationLevel.Debug));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id)));
        return output.ToArray();
    }
}
