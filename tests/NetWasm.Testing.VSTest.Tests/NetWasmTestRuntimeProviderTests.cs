using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Host;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NetWasm.Testing.VSTest.Configuration;
using NetWasm.Testing.VSTest.Hosting;
using NetWasm.Testing.VSTest.Tests.Hosting;

namespace NetWasm.Testing.VSTest.Tests;

public sealed class NetWasmTestRuntimeProviderTests
{
    [Fact]
    public void ExposesTheStandardRuntimeProviderIdentityAndConnectionContract()
    {
        var type = typeof(NetWasmTestRuntimeProvider);
        var provider = CreateSubject();

        Assert.Equal(
            NetWasmTestRuntimeProvider.ExtensionUri,
            Assert.Single(type.GetCustomAttributes(typeof(ExtensionUriAttribute), false)
                .Cast<ExtensionUriAttribute>()).ExtensionUri);
        Assert.Equal(
            NetWasmTestRuntimeProvider.FriendlyName,
            Assert.Single(type.GetCustomAttributes(typeof(FriendlyNameAttribute), false)
                .Cast<FriendlyNameAttribute>()).FriendlyName);
        Assert.IsAssignableFrom<ITestRuntimeProvider2>(provider);
        Assert.False(provider.Shared);
        var connection = provider.GetTestHostConnectionInfo();
        Assert.Equal("127.0.0.1:0", connection.Endpoint);
        Assert.Equal(ConnectionRole.Client, connection.Role);
        Assert.Equal(Transport.Sockets, connection.Transport);
    }

    [Fact]
    public void DefaultCompositionRecognizesTheCanonicalRunConfiguration()
    {
        const string runSettings = """
            <RunSettings>
              <RunConfiguration>
                <TargetFrameworkVersion>NetWasm,Version=v0.1</TargetFrameworkVersion>
              </RunConfiguration>
            </RunSettings>
            """;

        Assert.True(new NetWasmTestRuntimeProvider()
            .CanExecuteCurrentRunConfiguration(runSettings));
    }

    [Fact]
    public void SelectsOnlyRunSettingsAcceptedByTheNetWasmReader()
    {
        var calls = new List<string?>();
        var provider = CreateSubject(runSettings: new RunSettingsReaderStub((string? xml, out NetWasmRunConfiguration? configuration) =>
        {
            calls.Add(xml);
            configuration = xml == "accepted" ? Configuration() : null;
            return configuration is not null;
        }), initialize: false);

        Assert.True(provider.CanExecuteCurrentRunConfiguration("accepted"));
        Assert.False(provider.CanExecuteCurrentRunConfiguration("declined"));
        Assert.Equal(["accepted", "declined"], calls);
    }

    [Fact]
    public void BuildsStartInformationFromTheInitializedConfigurationAndPackageAssets()
    {
        var configuration = Configuration();
        var assets = Assets();
        var expected = new TestProcessStartInfo { FileName = "/expected/dotnet" };
        var source = Path.GetFullPath("Example.Tests.dll");
        var environment = new Dictionary<string, string?> { ["NAME"] = "value" };
        var connection = TestHostStartInfoBuilderTests.Connection();
        var provider = CreateSubject(
            dotnet: new InvokingDotnetResolverStub(value =>
            {
                Assert.Same(configuration, value);
                return "/tools/dotnet";
            }),
            assets: new PortableTestHostAssetResolverStub(() => assets),
            startInfos: new TestHostStartInfoBuilderStub((dotnet, actualAssets, workingDirectory, actualEnvironment, actualConnection) =>
            {
                Assert.Equal("/tools/dotnet", dotnet);
                Assert.Same(assets, actualAssets);
                Assert.Equal(Path.GetDirectoryName(source), workingDirectory);
                Assert.Same(environment, actualEnvironment);
                Assert.Equal(connection, actualConnection);
                return expected;
            }),
            configuration: configuration);

        var result = provider.GetTestHostProcessStartInfo([source], environment, connection);

        Assert.Same(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative.dll")]
    [InlineData("contains\0null.dll")]
    public void RejectsANonCanonicalTestSource(string? source)
    {
        var provider = CreateSubject(configuration: Configuration());

        Assert.Throws<ArgumentException>(() => provider.GetTestHostProcessStartInfo(
            [source!],
            null,
            TestHostStartInfoBuilderTests.Connection()));
    }

    [Fact]
    public void RejectsATestSourceWithNonCanonicalSegments()
    {
        var provider = CreateSubject(configuration: Configuration());
        var source = Path.Combine(Path.GetTempPath(), "folder", "..", "source.dll");

        Assert.Throws<ArgumentException>(() => provider.GetTestHostProcessStartInfo(
            [source],
            null,
            TestHostStartInfoBuilderTests.Connection()));
    }

    [Fact]
    public void RejectsAPathRootAsATestSource()
    {
        var provider = CreateSubject(configuration: Configuration());

        Assert.Throws<ArgumentException>(() => provider.GetTestHostProcessStartInfo(
            [Path.GetPathRoot(Path.GetTempPath())!],
            null,
            TestHostStartInfoBuilderTests.Connection()));
    }

    [Fact]
    public void RequiresInitializationBeforeBuildingStartInformation()
    {
        var provider = CreateSubject(initialize: false);

        Assert.Throws<InvalidOperationException>(() => provider.GetTestHostProcessStartInfo(
            [Path.GetFullPath("Example.Tests.dll")],
            null,
            TestHostStartInfoBuilderTests.Connection()));
    }

    [Fact]
    public async Task LaunchesThePackageProcessAndRaisesStandardLifecycleEvents()
    {
        var lifecycle = new List<string>();
        var process = new TestHostProcessStub(314);
        Action<string>? output = null;
        Action<string>? error = null;
        Action<int, int>? exited = null;
        var logger = new MessageLoggerStub();
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub(
                (startInfo, stdout, stderr, onExited) =>
                {
                    Assert.Equal("dotnet", startInfo.FileName);
                    output = stdout;
                    error = stderr;
                    exited = onExited;
                    return process;
                }),
            configuration: Configuration(),
            logger: logger);
        provider.HostLaunched += (_, eventArgs) =>
        {
            Assert.Equal(314, eventArgs.ProcessId);
            lifecycle.Add("launched");
        };
        provider.HostExited += (_, eventArgs) =>
        {
            Assert.Equal(314, eventArgs.ProcessId);
            Assert.Equal(7, eventArgs.ErrroCode);
            lifecycle.Add("exited");
        };

        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));
        output!("ordinary output");
        error!("ordinary error");
        exited!(314, 7);
        exited(314, 7);

        Assert.Equal(["launched", "exited"], lifecycle);
        Assert.Contains((TestMessageLevel.Informational, "ordinary output"), logger.Messages);
        Assert.Contains((TestMessageLevel.Error, "ordinary error"), logger.Messages);
        Assert.Equal(1, process.DisposeCount);
        Assert.Equal(0, process.TerminateCount);
    }

    [Fact]
    public async Task PreservesLaunchBeforeExitWhenTheTesthostExitsImmediately()
    {
        var lifecycle = new List<string>();
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, exited) =>
            {
                exited(99, 1);
                return new TestHostProcessStub(99);
            }),
            configuration: Configuration());
        provider.HostLaunched += (_, _) => lifecycle.Add("launched");
        provider.HostExited += (_, _) => lifecycle.Add("exited");

        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        Assert.Equal(["launched", "exited"], lifecycle);
    }

    [Fact]
    public async Task CleanupTerminatesOneActiveHostAndRaisesExitOnlyOnce()
    {
        var exits = 0;
        var process = new TestHostProcessStub(2718);
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) => process),
            configuration: Configuration());
        provider.HostExited += (_, _) => exits++;
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        await provider.CleanTestHostAsync(CancellationToken.None);
        await provider.CleanTestHostAsync(CancellationToken.None);

        Assert.Equal(1, process.TerminateCount);
        Assert.Equal(1, process.DisposeCount);
        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task LaunchFailureReturnsFalseAndReportsOnlyACategoricalMessage()
    {
        var logger = new MessageLoggerStub();
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) =>
                throw new InvalidOperationException("private launch detail")),
            configuration: Configuration(),
            logger: logger);

        Assert.False(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        var message = Assert.Single(logger.Messages);
        Assert.Equal(TestMessageLevel.Error, message.Level);
        Assert.DoesNotContain("private", message.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedLaunchCanBeRetried()
    {
        var attempts = 0;
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) =>
            {
                attempts++;
                return attempts == 1
                    ? throw new InvalidOperationException("first attempt")
                    : new TestHostProcessStub(23);
            }),
            configuration: Configuration());

        Assert.False(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task PropagatesLaunchCancellationAndAllowsARetry()
    {
        var attempts = 0;
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) =>
            {
                attempts++;
                return attempts == 1
                    ? throw new OperationCanceledException()
                    : new TestHostProcessStub(29);
            }),
            configuration: Configuration());

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await provider.LaunchTestHostAsync(
                new TestProcessStartInfo { FileName = "dotnet" },
                CancellationToken.None));
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RejectsASecondLaunchAndReinitializationWhileAHostIsActive()
    {
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) => new TestHostProcessStub(31)),
            configuration: Configuration());
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.LaunchTestHostAsync(
                new TestProcessStartInfo { FileName = "dotnet" },
                CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => provider.Initialize(null, "accepted"));
    }

    [Fact]
    public async Task RejectsReinitializationWhileAHostIsLaunching()
    {
        NetWasmTestRuntimeProvider? provider = null;
        provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) =>
            {
                Assert.Throws<InvalidOperationException>(() => provider!.Initialize(null, "accepted"));
                return new TestHostProcessStub(33);
            }),
            configuration: Configuration());

        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));
    }

    [Fact]
    public async Task RestoresLaunchStateAndPublishesAPendingExitWhenALaunchObserverThrows()
    {
        var attempts = 0;
        var exits = 0;
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, exited) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    exited(37, 2);
                }
                return new TestHostProcessStub(37 + attempts);
            }),
            configuration: Configuration());
        EventHandler<HostProviderEventArgs> observer = (_, _) =>
            throw new InvalidOperationException("observer failure");
        provider.HostLaunched += observer;
        provider.HostExited += (_, _) => exits++;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.LaunchTestHostAsync(
                new TestProcessStartInfo { FileName = "dotnet" },
                CancellationToken.None));
        provider.HostLaunched -= observer;
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        Assert.Equal(2, attempts);
        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task PreCancellationPreventsProcessLaunch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) =>
                throw new Xunit.Sdk.XunitException("Must not launch.")),
            configuration: Configuration());

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await provider.LaunchTestHostAsync(
                new TestProcessStartInfo { FileName = "dotnet" },
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task CleanupHonorsCancellationAfterReleasingTheActiveHost()
    {
        var process = new TestHostProcessStub(41);
        var provider = CreateSubject(
            processes: new TestHostProcessStarterStub((_, _, _, _) => process),
            configuration: Configuration());
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await provider.CleanTestHostAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, process.TerminateCount);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task UsesTheCustomLauncherAndAttachesToItsExactProcess()
    {
        var process = new TestHostProcessStub(43);
        var startInfo = new TestProcessStartInfo { FileName = "dotnet" };
        var (launcher, proxy) = Launcher<ITestHostLauncher>();
        proxy.InvokeMethod = (method, arguments) =>
        {
            Assert.Equal("LaunchTestHost", method.Name);
            Assert.Same(startInfo, arguments![0]);
            Assert.Equal(CancellationToken.None, arguments[1]);
            return 43;
        };
        var provider = CreateSubject(
            processAttachments: new TestHostProcessAttacherStub((processId, _) =>
            {
                Assert.Equal(43, processId);
                return process;
            }),
            configuration: Configuration());
        provider.SetCustomLauncher(launcher);

        Assert.True(await provider.LaunchTestHostAsync(startInfo, CancellationToken.None));
        Assert.Single(proxy.Invocations);
    }

    [Fact]
    public async Task DelegatesStructuredDebuggerAttachmentToLauncherThree()
    {
        var (launcher, proxy) = Launcher<ITestHostLauncher3>();
        proxy.InvokeMethod = (method, arguments) => method.Name switch
        {
            "LaunchTestHost" => 47,
            "AttachDebuggerToProcess" when arguments![0] is AttachDebuggerInfo info =>
                ObserveStructuredDebuggerAttachment(info, arguments),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected launcher call {method.Name}."),
        };
        var provider = CreateSubject(
            processAttachments: new TestHostProcessAttacherStub((_, _) => new TestHostProcessStub(47)),
            configuration: Configuration());
        provider.SetCustomLauncher(launcher);
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        Assert.True(provider.AttachDebuggerToTestHost());
        Assert.Equal(2, proxy.Invocations.Count);
    }

    [Fact]
    public async Task DelegatesLegacyDebuggerAttachmentToLauncherTwo()
    {
        var (launcher, proxy) = Launcher<ITestHostLauncher2>();
        proxy.InvokeMethod = (method, arguments) => method.Name switch
        {
            "LaunchTestHost" => 53,
            "AttachDebuggerToProcess" => ObserveLegacyDebuggerAttachment(arguments),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected launcher call {method.Name}."),
        };
        var provider = CreateSubject(
            processAttachments: new TestHostProcessAttacherStub((_, _) => new TestHostProcessStub(53)),
            configuration: Configuration());
        provider.SetCustomLauncher(launcher);
        Assert.True(await provider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        Assert.True(provider.AttachDebuggerToTestHost());
        Assert.Equal(2, proxy.Invocations.Count);
    }

    [Fact]
    public async Task DeclinesDebuggerAttachmentWithoutASupportedLauncherOrActiveHost()
    {
        var provider = CreateSubject(configuration: Configuration());
        Assert.False(provider.AttachDebuggerToTestHost());
        var (launcher, proxy) = Launcher<ITestHostLauncher>();
        proxy.InvokeMethod = (_, _) => 59;
        provider.SetCustomLauncher(launcher);
        var activeProvider = CreateSubject(
            processAttachments: new TestHostProcessAttacherStub((_, _) => new TestHostProcessStub(59)),
            configuration: Configuration());
        activeProvider.SetCustomLauncher(launcher);
        Assert.True(await activeProvider.LaunchTestHostAsync(
            new TestProcessStartInfo { FileName = "dotnet" },
            CancellationToken.None));

        Assert.False(activeProvider.AttachDebuggerToTestHost());
    }

    [Fact]
    public void TransfersSourcesAndExtensionsWithoutDiscoveryOrMutation()
    {
        var provider = CreateSubject();
        var sources = new[] { "/one.dll", "/two.dll" };
        var extensions = new[] { "/framework/Example.TestAdapter.dll" };

        var transferredSources = provider.GetTestSources(sources);
        var transferredExtensions = provider.GetTestPlatformExtensions(sources, extensions);

        Assert.Equal(sources, transferredSources);
        Assert.Equal(extensions, transferredExtensions);
        Assert.NotSame(sources, transferredSources);
        Assert.NotSame(extensions, transferredExtensions);
    }

    [Fact]
    public async Task RejectsNullFrameworkInputsBeforeCallingCollaborators()
    {
        var provider = CreateSubject(configuration: Configuration());

        Assert.Throws<ArgumentNullException>(() => provider.GetTestHostProcessStartInfo(
            null!,
            null,
            TestHostStartInfoBuilderTests.Connection()));
        Assert.Throws<ArgumentNullException>(() => provider.GetTestSources(null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetTestPlatformExtensions([], null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetTestPlatformExtensions(null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await provider.LaunchTestHostAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsAnythingOtherThanOneTestSource(int count)
    {
        var provider = CreateSubject(configuration: Configuration());
        var sources = Enumerable.Range(0, count)
            .Select(index => Path.GetFullPath($"source-{index}.dll"));

        Assert.Throws<ArgumentException>(() => provider.GetTestHostProcessStartInfo(
            sources,
            null,
            TestHostStartInfoBuilderTests.Connection()));
    }

    [Fact]
    public void RejectsInvalidInitializationAndMissingDependencies()
    {
        var provider = CreateSubject(runSettings: new RunSettingsReaderStub((string? _, out NetWasmRunConfiguration? configuration) =>
        {
            configuration = null;
            return false;
        }), initialize: false);
        Assert.Throws<ArgumentException>(() => provider.Initialize(null, "invalid"));

        var runSettings = new RunSettingsReaderStub((string? _, out NetWasmRunConfiguration? value) =>
        {
            value = null;
            return false;
        });
        var dotnet = new InvokingDotnetResolverStub(_ => throw new NotImplementedException());
        var assets = new PortableTestHostAssetResolverStub(() => throw new NotImplementedException());
        var startInfos = new TestHostStartInfoBuilderStub((_, _, _, _, _) => throw new NotImplementedException());
        var processes = new TestHostProcessStarterStub((_, _, _, _) => throw new NotImplementedException());
        var attachments = new TestHostProcessAttacherStub((_, _) => throw new NotImplementedException());

        Assert.Throws<ArgumentNullException>(() => new NetWasmTestRuntimeProvider(
            null!, dotnet, assets, startInfos, processes, attachments));
        Assert.Throws<ArgumentNullException>(() => new NetWasmTestRuntimeProvider(
            runSettings, null!, assets, startInfos, processes, attachments));
        Assert.Throws<ArgumentNullException>(() => new NetWasmTestRuntimeProvider(
            runSettings, dotnet, null!, startInfos, processes, attachments));
        Assert.Throws<ArgumentNullException>(() => new NetWasmTestRuntimeProvider(
            runSettings, dotnet, assets, null!, processes, attachments));
        Assert.Throws<ArgumentNullException>(() => new NetWasmTestRuntimeProvider(
            runSettings, dotnet, assets, startInfos, null!, attachments));
        Assert.Throws<ArgumentNullException>(() => new NetWasmTestRuntimeProvider(
            runSettings, dotnet, assets, startInfos, processes, null!));

        Assert.Throws<ArgumentNullException>(() => CreateSubject().SetCustomLauncher(null!));
    }

    private static bool ObserveStructuredDebuggerAttachment(
        AttachDebuggerInfo info,
        object?[]? arguments)
    {
        Assert.Equal(47, info.ProcessId);
        Assert.Equal(NetWasmRunSettingsReader.SupportedTargetFrameworkMoniker, info.TargetFramework);
        Assert.Equal(CancellationToken.None, arguments![1]);
        return true;
    }

    private static bool ObserveLegacyDebuggerAttachment(object?[]? arguments)
    {
        Assert.Equal(53, arguments![0]);
        return true;
    }

    private static (TLauncher Launcher, LauncherDispatchProxy Proxy) Launcher<TLauncher>()
        where TLauncher : class
    {
        var launcher = DispatchProxy.Create<TLauncher, LauncherDispatchProxy>();
        return (launcher, (LauncherDispatchProxy)(object)launcher);
    }

    private static NetWasmTestRuntimeProvider CreateSubject(
        INetWasmRunSettingsReader? runSettings = null,
        IInvokingDotnetResolver? dotnet = null,
        IPortableTestHostAssetResolver? assets = null,
        ITestHostStartInfoBuilder? startInfos = null,
        ITestHostProcessStarter? processes = null,
        ITestHostProcessAttacher? processAttachments = null,
        NetWasmRunConfiguration? configuration = null,
        IMessageLogger? logger = null,
        bool initialize = true)
    {
        configuration ??= Configuration();
        var subject = new NetWasmTestRuntimeProvider(
            runSettings ?? new RunSettingsReaderStub((string? _, out NetWasmRunConfiguration? value) =>
            {
                value = configuration;
                return true;
            }),
            dotnet ?? new InvokingDotnetResolverStub(_ => throw new NotImplementedException()),
            assets ?? new PortableTestHostAssetResolverStub(() => throw new NotImplementedException()),
            startInfos ?? new TestHostStartInfoBuilderStub((_, _, _, _, _) => throw new NotImplementedException()),
            processes ?? new TestHostProcessStarterStub((_, _, _, _) => throw new NotImplementedException()),
            processAttachments ?? new TestHostProcessAttacherStub((_, _) => throw new NotImplementedException()));
        if (initialize)
        {
            subject.Initialize(logger, "accepted");
        }
        return subject;
    }

    private static NetWasmRunConfiguration Configuration() => new(
        NetWasmRunSettingsReader.SupportedTargetFrameworkMoniker,
        null);

    private static PortableTestHostAssets Assets()
    {
        var root = Path.GetFullPath("testhost");
        return new PortableTestHostAssets(
            Path.Combine(root, "testhost.dll"),
            Path.Combine(root, "testhost.deps.json"),
            Path.Combine(root, "testhost.runtimeconfig.json"),
            Path.Combine(root, "packages"));
    }
}

internal delegate bool TryReadRunSettings(
    string? runsettingsXml,
    out NetWasmRunConfiguration? configuration);

internal sealed class RunSettingsReaderStub(TryReadRunSettings read) : INetWasmRunSettingsReader
{
    public bool TryRead(string? runsettingsXml, out NetWasmRunConfiguration? configuration) =>
        read(runsettingsXml, out configuration);
}

internal sealed class InvokingDotnetResolverStub(Func<NetWasmRunConfiguration, string> resolve)
    : IInvokingDotnetResolver
{
    public string Resolve(NetWasmRunConfiguration configuration) => resolve(configuration);
}

internal sealed class PortableTestHostAssetResolverStub(Func<PortableTestHostAssets> resolve)
    : IPortableTestHostAssetResolver
{
    public PortableTestHostAssets Resolve() => resolve();
}

internal sealed class TestHostStartInfoBuilderStub(
    Func<string, PortableTestHostAssets, string, IDictionary<string, string?>?,
        TestRunnerConnectionInfo, TestProcessStartInfo> build) : ITestHostStartInfoBuilder
{
    public TestProcessStartInfo Build(
        string dotnetPath,
        PortableTestHostAssets assets,
        string workingDirectory,
        IDictionary<string, string?>? environmentVariables,
        TestRunnerConnectionInfo connectionInfo) =>
        build(dotnetPath, assets, workingDirectory, environmentVariables, connectionInfo);
}

internal sealed class TestHostProcessStarterStub(
    Func<TestProcessStartInfo, Action<string>, Action<string>, Action<int, int>, ITestHostProcess> start)
    : ITestHostProcessStarter
{
    public ITestHostProcess Start(
        TestProcessStartInfo startInfo,
        Action<string> standardOutput,
        Action<string> standardError,
        Action<int, int> exited) =>
        start(startInfo, standardOutput, standardError, exited);

}

internal sealed class TestHostProcessAttacherStub(
    Func<int, Action<int, int>, ITestHostProcess> attach) : ITestHostProcessAttacher
{
    public ITestHostProcess Attach(int processId, Action<int, int> exited) =>
        attach(processId, exited);
}

internal sealed class TestHostProcessStub(int id) : ITestHostProcess
{
    public int Id { get; } = id;
    internal int TerminateCount { get; private set; }
    internal int DisposeCount { get; private set; }

    public void Terminate() => TerminateCount++;

    public void Dispose() => DisposeCount++;
}

internal sealed class MessageLoggerStub : IMessageLogger
{
    internal List<(TestMessageLevel Level, string Message)> Messages { get; } = [];

    public void SendMessage(TestMessageLevel testMessageLevel, string message) =>
        Messages.Add((testMessageLevel, message));
}

[SuppressMessage(
    "Performance",
    "CA1852:Seal internal types",
    Justification = "DispatchProxy generates a runtime subclass of this test proxy.")]
internal class LauncherDispatchProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> InvokeMethod { get; set; } =
        (_, _) => throw new NotImplementedException();

    internal List<(MethodInfo Method, object?[]? Arguments)> Invocations { get; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        Invocations.Add((targetMethod, arguments));
        return InvokeMethod(targetMethod, arguments);
    }
}
