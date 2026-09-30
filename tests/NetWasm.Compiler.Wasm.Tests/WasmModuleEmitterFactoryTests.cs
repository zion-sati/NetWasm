using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmModuleEmitterFactoryTests
{
    [Fact]
    public void RetainsReleasedOneLoggerConstructor()
    {
        var constructor = typeof(WasmModuleEmitterFactory).GetConstructor(
            [typeof(ILogger<WasmModuleEmitterFactory>)]);

        Assert.NotNull(constructor);
        Assert.True(constructor.GetParameters()[0].IsOptional);
        Assert.IsType<WasmModuleEmitterFactory>(constructor.Invoke([null]));
    }

    [Fact]
    public void ComposesAndDisposesAFreshEmitterGraphPerModule()
    {
        var program = new FakeProgram();
        var request = CreateEmissionRequest(program);
        var factory = new WasmModuleEmitterFactory();

        var first = EmitModule(
            factory,
            program,
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            request);
        var second = EmitModule(
            factory,
            program,
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            request);

        Assert.NotEmpty(first.Module);
        Assert.Equal(first.Module, second.Module);
        Assert.Equal(first.StaticDataEnd, second.StaticDataEnd);
    }

    [Fact]
    public void ParallelCompositionUsesRequestOwnedLayoutForks()
    {
        var program = new FakeProgram();
        var request = CreateEmissionRequest(program);
        var layouts = new RecordingLayoutProvider();
        var forks = new RecordingLayoutForkSourceFactory(layouts);
        var factory = new WasmModuleEmitterFactory(
            logger: null,
            forks,
            new CompilerParallelism(2));

        var result = EmitModule(
            factory,
            program,
            new FakeIntrinsics(),
            layouts,
            request);

        Assert.NotEmpty(result.Module);
        Assert.Equal(1, forks.CreateCount);
        Assert.Equal(2, forks.Source.WorkerCount);
        Assert.True(forks.Source.Publisher.PublishCount > 0);
    }

    private sealed class RecordingLayoutForkSourceFactory(
        RecordingLayoutProvider layouts) : IManagedLayoutForkSourceFactory
    {
        public int CreateCount { get; private set; }
        public RecordingLayoutForkSource Source { get; } = new(layouts);

        public IManagedLayoutForkSource Create(
            ITargetLayout layout,
            ITypeRepository types,
            ITypeDefinitionResolver typeDefinitions,
            IFieldRepository fields)
        {
            CreateCount++;
            return Source;
        }
    }

    private sealed class RecordingLayoutForkSource(
        RecordingLayoutProvider layouts) : IManagedLayoutForkSource
    {
        public int WorkerCount { get; private set; }
        public RecordingLayoutForkPublisher Publisher { get; } = new();

        public ManagedLayoutForks Create(int workerCount)
        {
            WorkerCount = workerCount;
            return new(
                Enumerable.Range(0, workerCount)
                    .Select(_ => new ManagedLayoutFork(layouts, layouts))
                    .ToImmutableArray(),
                Publisher);
        }
    }

    private sealed class RecordingLayoutForkPublisher : IManagedLayoutForkPublisher
    {
        public int PublishCount { get; private set; }

        public void Publish() => PublishCount++;
    }
}
