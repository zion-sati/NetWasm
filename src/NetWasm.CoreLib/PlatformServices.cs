namespace System.Runtime.InteropServices
{
    using CommandLine;
    using WebAssembly;
    using Threading.Tasks;
    using TimeZones;

    internal readonly struct PlatformTimestamp
    {
        internal PlatformTimestamp(ulong seconds, uint nanoseconds)
        {
            if (nanoseconds >= 1_000_000_000)
            {
                throw new ArgumentException();
            }
            Seconds = seconds;
            Nanoseconds = nanoseconds;
        }

        internal ulong Seconds { get; }
        internal uint Nanoseconds { get; }
    }

    internal interface IPlatformUtcClockSource
    {
        PlatformTimestamp GetUtcTime();
    }

    internal interface IPlatformMonotonicClockSource
    {
        ulong GetMonotonicTime();
    }

    internal interface IPlatformSchedule : IDisposable
    {
    }

    internal interface IPlatformScheduler
    {
        IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds);
    }

    internal static class WasiPlatformServicesComposition
    {
        internal static ILocalTimeOffsetResolver CreateLocalTimeOffsetResolver() =>
            new UtcLocalTimeOffsetResolver();

        internal static ILocalTimePreflightValidator CreateLocalTimePreflightValidator() =>
            new LocalTimePreflightValidator(
                new EnvironmentVariableReader(PlatformServices.ProcessEnvironment),
                new TimeZoneAssetFileOpener(
                    new WasiPreopenedDirectorySource(new WasiPreopensInvoker()),
                    new WasiReadOnlyFileOpener(new WasiDescriptorOpenInvoker(
                        new WasiOpenAtInvoker(),
                        new Concurrency.SubtaskReturnAwaiter(
                            CreateWaitableEventAwaiter(),
                            new Concurrency.WasiSubtaskDropInvoker()),
                        new CanonicalBufferAllocator())),
                    new WasiReadOnlyFileReader(
                        new WasiFileStreamOpenInvoker(),
                        new WasiFileStreamReadInvoker(),
                        new WasiFileStreamReadableDropInvoker(),
                        new WasiFileStreamResultReadInvoker(),
                        new WasiFileStreamResultDropInvoker(),
                        CreateWaitableEventAwaiter(),
                        new CanonicalBufferAllocator())),
                new TimeZoneAssetParser(),
                new TimeZoneDefinitionResolverFactory(new TimeZoneTransitionTableReader()),
                new LocalTimeOffsetResolverFactory(new TimeZoneTransitionResolver()),
                new UtcLocalTimeOffsetResolver(),
                new LocalTimeOffsetResolverInstaller(),
                new TimeZoneCatalogSnapshotInstaller());

        internal static IManagedEntryPointArgumentSource
            CreateManagedEntryPointArgumentSource(
                IProcessEnvironment commandLine) =>
            new ManagedEntryPointArgumentSource(commandLine);

        internal static CommandWorldScheduler CreateCommandWorldScheduler() =>
            new(
                PlatformWorld.ImportsMonotonicClock()
                    ? new Concurrency.WasiMonotonicWaitInvoker()
                    : new Concurrency.AbsentMonotonicWaitInvoker(),
                new Concurrency.WasiWaitableSetCreateInvoker(),
                new Concurrency.WasiWaitableJoinInvoker(),
                new Concurrency.WasiSubtaskCancelInvoker(),
                new Concurrency.WasiSubtaskDropInvoker())
            {
                HostCanComplete = PlatformWorld.HasHostEventLoop(),
            };

        internal static IPlatformTimerFactory CreateTimerFactory(
            IPlatformScheduler scheduler) =>
            new PlatformTimerFactory(scheduler);

        internal static Concurrency.IWaitableEventAwaiter CreateWaitableEventAwaiter() =>
            new Concurrency.WaitableEventAwaiter(
                new Concurrency.WasiWaitableSetCreateInvoker(),
                new Concurrency.WasiWaitableJoinInvoker(),
                new Concurrency.WasiWaitableSetWaitInvoker());

        // Each standard stream is composed on its own first use, so a program that writes only
        // standard output reaches none of standard error's bindings.
        internal static Output.WasiStreamBlockingOutputWriter CreateStandardOutput()
        {
            var awaiter = CreateWaitableEventAwaiter();
            return new Output.WasiStreamBlockingOutputWriter(
                new Output.WasiOutputStreamResource(
                    new Output.WasiOutputStreamOpener(
                        new Output.WasiStandardOutputStreamCreateInvoker(),
                        new Output.WasiStandardOutputHandOffInvoker()),
                    new Output.WasiOutputStreamCloser(
                        new Output.WasiStandardOutputWritableDropInvoker(),
                        new Output.WasiStandardOutputResultReadInvoker(),
                        new Output.WasiStandardOutputResultDropInvoker(),
                        awaiter,
                        new CanonicalBufferAllocator()),
                    new Output.WasiStandardOutputWritableDropInvoker()),
                new Output.WasiStandardOutputWriteInvoker(),
                awaiter);
        }

        internal static Input.WasiStandardInputReader CreateStandardInput() =>
            new(
                new Input.WasiStandardInputOpenInvoker(),
                new Input.WasiStandardInputReadInvoker(),
                new Input.WasiStandardInputReadableDropInvoker(),
                new Input.WasiStandardInputResultReadInvoker(),
                new Input.WasiStandardInputResultDropInvoker(),
                CreateWaitableEventAwaiter(),
                new CanonicalBufferAllocator());

        internal static Output.WasiStreamBlockingOutputWriter CreateStandardError()
        {
            var awaiter = CreateWaitableEventAwaiter();
            return new Output.WasiStreamBlockingOutputWriter(
                new Output.WasiOutputStreamResource(
                    new Output.WasiOutputStreamOpener(
                        new Output.WasiStandardErrorStreamCreateInvoker(),
                        new Output.WasiStandardErrorHandOffInvoker()),
                    new Output.WasiOutputStreamCloser(
                        new Output.WasiStandardErrorWritableDropInvoker(),
                        new Output.WasiStandardErrorResultReadInvoker(),
                        new Output.WasiStandardErrorResultDropInvoker(),
                        awaiter,
                        new CanonicalBufferAllocator()),
                    new Output.WasiStandardErrorWritableDropInvoker()),
                new Output.WasiStandardErrorWriteInvoker(),
                awaiter);
        }
    }

    // System APIs such as DateTime.UtcNow and Task.Delay are framework-mandated
    // static entry points. This type is their composition boundary, not a
    // service locator: it stores the selected capabilities directly and never
    // resolves a service by type or name.
    internal static class PlatformServices
    {
        private static NetWasmPlatform? _platform;
        private static IStandardStreams? _standardStreams;
        private static IClock? _clock;
        private static IRandomSource? _random;
        private static IProcessEnvironment? _processEnvironment;
        private static IProcessExit? _processExit;
        private static IFileSystem? _fileSystem;
        private static INetwork? _network;
        private static IManagedEntryPointArgumentSource? _managedEntryPointArguments;
        private static CommandWorldScheduler? _commandScheduler;
        private static IPlatformScheduler? _scheduler;
        private static IPlatformTimerFactory? _timerFactory;
        private static ILocalTimeOffsetResolver? _localTimeOffsets;
        private static TimeZoneCatalogSnapshot? _timeZoneCatalogSnapshot;
        private static bool _localTimeReady;

        // The program's platform (PlatformServiceBindings), created on first use; each facade is
        // read from it once, when the program first uses that area.
        internal static NetWasmPlatform Platform => _platform ??= PlatformServiceBindings.CreatePlatform();

        internal static IStandardStreams StandardStreams => _standardStreams ??= Platform.StandardStreams;

        internal static IClock Clock => _clock ??= Platform.Clock;

        internal static IRandomSource Random => _random ??= Platform.Random;

        internal static IProcessEnvironment ProcessEnvironment => _processEnvironment ??= Platform.ProcessEnvironment;

        internal static IProcessExit ProcessExit => _processExit ??= Platform.ProcessExit;

        internal static IFileSystem FileSystem => _fileSystem ??= Platform.FileSystem;

        internal static INetwork Network => _network ??= Platform.Network;

        internal static string[] ReadCommandLineArguments() => ProcessEnvironment.GetArguments();

        internal static IManagedEntryPointArgumentSource ManagedEntryPointArguments =>
            _managedEntryPointArguments ??=
                WasiPlatformServicesComposition.CreateManagedEntryPointArgumentSource(ProcessEnvironment);

        internal static string[] ReadManagedEntryPointArguments() =>
            ManagedEntryPointArguments.Read();

        internal static IPlatformScheduler Scheduler
        {
            get
            {
                if (_scheduler == null)
                {
                    ComposeSchedulers();
                }
                return _scheduler!;
            }
        }

        internal static IPlatformTimerFactory TimerFactory =>
            _timerFactory ??=
                WasiPlatformServicesComposition.CreateTimerFactory(Scheduler);

        internal static ILocalTimeOffsetResolver LocalTimeOffsets =>
            _localTimeOffsets ??=
                WasiPlatformServicesComposition.CreateLocalTimeOffsetResolver();

        internal static void UseLocalTimeOffsetResolver(
            ILocalTimeOffsetResolver resolver)
        {
            _localTimeOffsets = resolver ?? throw new ArgumentNullException();
        }

        internal static TimeZoneCatalogSnapshot TimeZoneCatalogSnapshot =>
            _timeZoneCatalogSnapshot ??=
                new TimeZoneCatalogSnapshot(null, null, null);

        internal static void UseTimeZoneCatalogSnapshot(
            TimeZoneCatalogSnapshot snapshot)
        {
            _timeZoneCatalogSnapshot = snapshot ?? throw new ArgumentNullException();
        }

        // The local time zone comes from WASI's environment (TZ) and the time zone asset; a plain
        // core module has neither, so its local time is UTC.
        internal static void PreflightLocalTime()
        {
            if (PlatformWorld.ProvidesWasi())
            {
                WasiPlatformServicesComposition.CreateLocalTimePreflightValidator().Validate();
            }
        }

        // A component runs its own event loop on the Component Model's waitable sets; a core
        // module has none and defers work it cannot wait for.
        private static void ComposeSchedulers()
        {
            if (!PlatformWorld.ImportsEventLoop())
            {
                // A plain core module runs its ready work itself and takes delays from the
                // program's timer binding; a raw module without a world defers its work.
                _scheduler = PlatformWorld.ProvidesWasi()
                    ? new UnpumpedScheduler()
                    : new CoreModuleScheduler(Platform.Timers);
            }
            else
            {
                var scheduler = WasiPlatformServicesComposition.CreateCommandWorldScheduler();
                _scheduler = scheduler;
                _commandScheduler = scheduler;
            }
        }

        // A process entry calls one of these after Main returns: an int Main's result is the exit
        // code, any other Main's is Environment.ExitCode. The standard streams the process opened
        // end, as an operating system closes a process's descriptors at exit, and a non-zero exit
        // code ends the process with that code, which wasi:cli/run's result cannot carry.
        internal static void RunProcessExit() => RunProcessExit(Environment.ExitCode);

        // A world without wasi:cli/exit leaves an int Main's result to wasi:cli/run's result,
        // which carries only success or failure.
        internal static void RunProcessExit(int exitCode)
        {
            Output.WasiOutputStreamResource.EndOpened();
            if (exitCode != 0 && PlatformWorld.ImportsProcessExit())
            {
                ProcessExit.Exit(exitCode);
            }
        }

        // Environment.Exit: the process ends here, whatever the code.
        [Diagnostics.CodeAnalysis.DoesNotReturn]
        internal static void ExitProcess(int exitCode)
        {
            Output.WasiOutputStreamResource.EndOpened();
            ProcessExit.Exit(exitCode);
            throw new Diagnostics.UnreachableException("The platform returned from its process exit.");
        }

        // Environment.FailFast: the report goes to standard error, bypassing Console, and the process
        // ends as an abort ends .NET on Linux, with exit status 134. An exit, unlike an exception,
        // cannot be caught.
        [Diagnostics.CodeAnalysis.DoesNotReturn]
        internal static void FailProcess(string report)
        {
            StandardStreams.WriteError(Text.Encoding.UTF8.GetBytes(report));
            ExitProcess(134);
        }

        private static ComponentExportTask? _exportTask;

        internal static ComponentExportTask ExportTask => _exportTask ??= new ComponentExportTask(
            CommandSchedulerForCallbacks,
            static () => _commandScheduler?.AwaitPending() ?? 0,
            new Concurrency.ComponentBackpressure());

        // An async-lowered import's status: RETURNED is complete; otherwise its subtask joins the
        // event loop's waitable set and the task completes when it returns, in a later callback.
        internal static Task AwaitSubtask(int status)
        {
            if ((status & 0xF) == 2)
            {
                return Task.CompletedTask;
            }

            if (Scheduler is not CommandWorldScheduler loop)
            {
                throw new PlatformNotSupportedException(
                    "An asynchronous import needs the Component Model's event loop, which a core module does not have.");
            }

            var completion = new TaskCompletionSource();
            loop.Track(completion.SetResult, status);
            return completion.Task;
        }

        // A stream or future copy's status: BLOCKED (-1) completes with the result its event
        // carries; any other status is already the copy's result.
        internal static Task<int> AwaitCopy(int waitable, int status)
        {
            if (status != -1)
            {
                return Task.FromResult(status);
            }

            if (Scheduler is not CommandWorldScheduler loop)
            {
                throw new PlatformNotSupportedException(
                    "An asynchronous stream or future needs the Component Model's event loop, which a core module does not have.");
            }

            var completion = new TaskCompletionSource<int>();
            loop.TrackCopy(payload => completion.SetResult(unchecked((int)payload)), waitable);
            return completion.Task;
        }

        // A callback-lifted entry's start and callbacks: the event loop runs in the host's callbacks
        // instead of blocking. A world without the event loop can only complete a finished task.
        internal static int StartEntryPoint(Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException();
            }

            return task.IsCompleted ? 0 : CommandSchedulerForCallbacks().Start(task);
        }

        internal static int DeliverEntryPointEvent(Task task, int eventCode, int waitable, uint payload) =>
            CommandSchedulerForCallbacks().Deliver(task, eventCode, waitable, payload);

        // A JavaScript module's event loop, which its host runs after each call into the module.
        // A module that never scheduled anything has nothing to run.
        internal static int RunHostEventLoop() => _commandScheduler?.RunHost() ?? 0;

        internal static int DeliverHostEventLoopEvent(int eventCode, int waitable, uint payload)
        {
            if (_commandScheduler == null)
            {
                throw new InvalidOperationException("The event loop received an event for no scheduled work.");
            }

            return _commandScheduler.DeliverHost(eventCode, waitable, payload);
        }

        private static CommandWorldScheduler CommandSchedulerForCallbacks()
        {
            if (!PlatformWorld.ImportsEventLoop())
            {
                throw new PlatformNotSupportedException(
                    "The asynchronous entry point cannot complete: a core module has no event loop.");
            }

            // An entry point that so far only awaits its host, such as a JavaScript promise, has
            // scheduled nothing; the event loop still decides whether it can complete.
            _ = Scheduler;
            return _commandScheduler!;
        }

        internal static void EnsureLocalTimeReady()
        {
            if (_localTimeReady)
            {
                return;
            }
            PreflightLocalTime();
            _localTimeReady = true;
        }

    }
}
