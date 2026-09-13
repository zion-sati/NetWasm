using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Host;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NetWasm.Testing.VSTest.Configuration;
using NetWasm.Testing.VSTest.Hosting;

namespace NetWasm.Testing.VSTest;

[ExtensionUri(ExtensionUri)]
[FriendlyName(FriendlyName)]
public sealed class NetWasmTestRuntimeProvider : ITestRuntimeProvider2
{
    internal const string ExtensionUri = "hostProvider://NetWasmTestHost";
    internal const string FriendlyName = "NetWasmTestHost";
    private readonly object _gate = new();
    private readonly INetWasmRunSettingsReader _runSettings;
    private readonly IInvokingDotnetResolver _dotnet;
    private readonly IPortableTestHostAssetResolver _assets;
    private readonly ITestHostStartInfoBuilder _startInfos;
    private readonly ITestHostProcessStarter _processes;
    private readonly ITestHostProcessAttacher _processAttachments;
    private NetWasmRunConfiguration? _configuration;
    private IMessageLogger? _logger;
    private ITestHostLauncher? _customLauncher;
    private ITestHostProcess? _process;
    private (int ProcessId, int ExitCode)? _pendingExit;
    private bool _launching;
    private bool _hostExitedRaised;

    public NetWasmTestRuntimeProvider()
        : this(
            new NetWasmRunSettingsReader(),
            new InvokingDotnetResolver(),
            new PortableTestHostAssetResolver(
                Path.GetDirectoryName(typeof(NetWasmTestRuntimeProvider).Assembly.Location)!),
            new TestHostStartInfoBuilder(),
            new TestHostProcessStarter(new TestHostProcessTerminator()),
            new TestHostProcessAttacher(new TestHostProcessTerminator()))
    {
    }

    internal NetWasmTestRuntimeProvider(
        INetWasmRunSettingsReader runSettings,
        IInvokingDotnetResolver dotnet,
        IPortableTestHostAssetResolver assets,
        ITestHostStartInfoBuilder startInfos,
        ITestHostProcessStarter processes,
        ITestHostProcessAttacher processAttachments)
    {
        _runSettings = runSettings ?? throw new ArgumentNullException(nameof(runSettings));
        _dotnet = dotnet ?? throw new ArgumentNullException(nameof(dotnet));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _startInfos = startInfos ?? throw new ArgumentNullException(nameof(startInfos));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _processAttachments = processAttachments
            ?? throw new ArgumentNullException(nameof(processAttachments));
    }

    public event EventHandler<HostProviderEventArgs>? HostLaunched;

    public event EventHandler<HostProviderEventArgs>? HostExited;

    public bool Shared => false;

    public void Initialize(IMessageLogger? logger, string runsettingsXml)
    {
        if (!_runSettings.TryRead(runsettingsXml, out var configuration)
            || configuration is null)
        {
            throw new ArgumentException(
                "The NetWasm runtime provider requires the NetWasm,Version=v0.1 run configuration.",
                nameof(runsettingsXml));
        }

        lock (_gate)
        {
            if (_process is not null || _launching)
            {
                throw new InvalidOperationException(
                    "The NetWasm runtime provider cannot be reinitialized while its testhost is active.");
            }
            _configuration = configuration;
            _logger = logger;
            _hostExitedRaised = false;
        }
    }

    public bool CanExecuteCurrentRunConfiguration(string? runsettingsXml) =>
        _runSettings.TryRead(runsettingsXml, out _);

    public void SetCustomLauncher(ITestHostLauncher customLauncher) =>
        _customLauncher = customLauncher ?? throw new ArgumentNullException(nameof(customLauncher));

    public TestHostConnectionInfo GetTestHostConnectionInfo() => new()
    {
        Endpoint = "127.0.0.1:0",
        Role = ConnectionRole.Client,
        Transport = Transport.Sockets,
    };

    public TestProcessStartInfo GetTestHostProcessStartInfo(
        IEnumerable<string> sources,
        IDictionary<string, string?>? environmentVariables,
        TestRunnerConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var sourceSnapshot = sources.Take(2).ToArray();
        if (sourceSnapshot.Length != 1)
        {
            throw new ArgumentException(
                "The NetWasm runtime provider requires exactly one test source.",
                nameof(sources));
        }
        var source = sourceSnapshot[0];
        if (!IsCanonicalFullyQualifiedPath(source))
        {
            throw new ArgumentException(
                "The NetWasm test source must be a canonical fully qualified path.",
                nameof(sources));
        }
        var workingDirectory = Path.GetDirectoryName(source);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException(
                "The NetWasm test source must have a fully qualified parent directory.",
                nameof(sources));
        }

        NetWasmRunConfiguration configuration;
        lock (_gate)
        {
            configuration = _configuration
                ?? throw new InvalidOperationException(
                    "The NetWasm runtime provider must be initialized before resolving testhost start information.");
        }

        return _startInfos.Build(
            _dotnet.Resolve(configuration),
            _assets.Resolve(),
            workingDirectory,
            environmentVariables,
            connectionInfo);
    }

    public Task<bool> LaunchTestHostAsync(
        TestProcessStartInfo testHostStartInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(testHostStartInfo);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_process is not null || _launching)
            {
                throw new InvalidOperationException("A NetWasm testhost is already active.");
            }
            _launching = true;
            _pendingExit = null;
            _hostExitedRaised = false;
        }

        ITestHostProcess process;
        try
        {
            process = _customLauncher is null
                ? _processes.Start(
                    testHostStartInfo,
                    message => Log(TestMessageLevel.Informational, message),
                    message => Log(TestMessageLevel.Error, message),
                    OnHostExited)
                : _processAttachments.Attach(
                    _customLauncher.LaunchTestHost(testHostStartInfo, cancellationToken),
                    OnHostExited);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _launching = false;
                _pendingExit = null;
            }
            throw;
        }
        catch (Exception)
        {
            lock (_gate)
            {
                _launching = false;
                _pendingExit = null;
            }
            Log(TestMessageLevel.Error, "The packaged NetWasm testhost could not be launched.");
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            _process = process;
        }
        (int ProcessId, int ExitCode)? pendingExit = null;
        try
        {
            HostLaunched?.Invoke(
                this,
                new HostProviderEventArgs("NetWasm testhost launched.", 0, process.Id));
        }
        finally
        {
            lock (_gate)
            {
                pendingExit = _pendingExit;
                _launching = false;
                _pendingExit = null;
            }
            if (pendingExit is { } exited)
            {
                OnHostExited(exited.ProcessId, exited.ExitCode);
            }
        }
        return Task.FromResult(true);
    }

    public IEnumerable<string> GetTestPlatformExtensions(
        IEnumerable<string> sources,
        IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(extensions);
        _ = sources.ToArray();
        return extensions.ToArray();
    }

    public IEnumerable<string> GetTestSources(IEnumerable<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources.ToArray();
    }

    public Task CleanTestHostAsync(CancellationToken cancellationToken)
    {
        ITestHostProcess? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }
        if (process is not null)
        {
            var processId = process.Id;
            process.Terminate();
            process.Dispose();
            RaiseHostExited(processId, 0);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public bool AttachDebuggerToTestHost()
    {
        ITestHostProcess? process;
        NetWasmRunConfiguration? configuration;
        lock (_gate)
        {
            process = _process;
            configuration = _configuration;
        }
        if (process is null || configuration is null)
        {
            return false;
        }

        return _customLauncher switch
        {
            ITestHostLauncher3 launcher => launcher.AttachDebuggerToProcess(
                new AttachDebuggerInfo
                {
                    ProcessId = process.Id,
                    TargetFramework = configuration.TargetFrameworkMoniker,
                },
                CancellationToken.None),
            ITestHostLauncher2 launcher => launcher.AttachDebuggerToProcess(process.Id),
            _ => false,
        };
    }

    private void OnHostExited(int processId, int exitCode)
    {
        lock (_gate)
        {
            if (_launching)
            {
                _pendingExit = (processId, exitCode);
                return;
            }
            _process?.Dispose();
            _process = null;
        }
        RaiseHostExited(processId, exitCode);
    }

    private void RaiseHostExited(int processId, int exitCode)
    {
        lock (_gate)
        {
            if (_hostExitedRaised)
            {
                return;
            }
            _hostExitedRaised = true;
        }
        HostExited?.Invoke(
            this,
            new HostProviderEventArgs("NetWasm testhost exited.", exitCode, processId));
    }

    private void Log(TestMessageLevel level, string message)
    {
        IMessageLogger? logger;
        lock (_gate)
        {
            logger = _logger;
        }
        logger?.SendMessage(level, message);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsCanonicalFullyQualifiedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\0', StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        return string.Equals(Path.GetFullPath(path), path, PathComparison);
    }
}
