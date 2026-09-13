namespace System.Runtime.InteropServices
{
    using CommandLine;
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

    // Compatibility aggregate retained because existing CoreLib component
    // fixtures implement and inspect both clock readings through this type.
    // Production consumers use IPlatformUtcClock or IPlatformMonotonicClock.
    internal interface IPlatformClock :
        IPlatformUtcClock,
        IPlatformMonotonicClock
    {
    }

    internal interface IPlatformUtcClock
    {
        PlatformTimestamp GetUtcTime();
    }

    internal interface IPlatformMonotonicClock
    {
        ulong GetMonotonicTime();
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

    internal interface IPlatformPollableScheduler
    {
        IPlatformSchedule SchedulePollable(Action continuation, int pollable);
    }

    internal interface IPlatformPollableSubscription
    {
        int SubscribeDuration(long durationNanoseconds);
    }

    internal interface IPlatformReactorWatcher
    {
        void Watch(int pollable, uint token);
    }

    internal interface IPlatformReactorCancellation
    {
        void Cancel(uint token);
    }

    // Compatibility aggregate for the stream resource contract. The actual
    // readiness and write actors use the one-action interfaces below.
    internal interface IPlatformOutputStream :
        IDisposable,
        IPlatformOutputReadiness,
        IPlatformOutputWriter,
        IPlatformBlockingOutputWriter
    {
    }

    internal interface IPlatformOutputReadiness
    {
        ulong CheckWrite();
    }

    internal interface IPlatformOutputWriter
    {
        void Write(byte[] contents);
    }

    internal interface IPlatformBlockingOutputWriter
    {
        void WriteBlocking(byte[] contents);
    }

    internal interface IPlatformOutputErrorThrower
    {
        void Throw(nuint result, nuint payloadOffset);
    }

    internal interface IPlatformOutputStreamFactory
    {
        IPlatformOutputStream Create(int handle);
    }

    internal interface IPlatformOutputStreamHandleSource
    {
        int GetStandardOutput();

        int GetStandardError();
    }

    internal interface IPlatformOutputReadinessSource
    {
        ulong CheckWrite(int handle);
    }

    internal interface IPlatformOutputWriterSource
    {
        void Write(int handle, byte[] contents);
    }

    internal interface IPlatformBlockingOutputWriterSource
    {
        void WriteBlocking(int handle, byte[] contents);
    }

    internal interface IPlatformOutputResourceSource
    {
        void Drop(int handle);
    }

    internal interface IPlatformOutputErrorResource
    {
        void Drop(int handle);
    }

    internal interface IPlatformStreams
    {
        IPlatformOutputStream GetStandardOutput();

        IPlatformOutputStream GetStandardError();
    }

    internal static class WasiPlatformServicesComposition
    {
        internal static ILocalTimeOffsetResolver CreateLocalTimeOffsetResolver() =>
            new UtcLocalTimeOffsetResolver();

        internal static ILocalTimePreflightValidator CreateLocalTimePreflightValidator() =>
            new LocalTimePreflightValidator(
                CreateEnvironmentVariableReader(),
                new TimeZoneAssetByteReader(
                    new WasiPreopenedDirectorySource(new WasiPreopensInvoker()),
                    new WasiReadOnlyFileOpener(new WasiDescriptorOpenInvoker()),
                    new WasiReadOnlyFileReader(new WasiDescriptorReadInvoker()),
                    new WasiDescriptorReleaser()),
                new TimeZoneAssetParser(new TimeZoneAssetDigestCalculator()),
                new TimeZoneDefinitionResolverFactory(),
                new LocalTimeOffsetResolverFactory(new TimeZoneTransitionResolver()),
                new UtcLocalTimeOffsetResolver(),
                new LocalTimeOffsetResolverInstaller());

        internal static IPlatformUtcClock CreateUtcClock() =>
            new WasiUtcClock(new WasiUtcClockSource());

        internal static ICommandLineArgumentSource CreateCommandLineArgumentSource() =>
            new WasiCommandLineArgumentSource(new WasiCommandLineInvoker());

        internal static IEnvironmentVariableReader CreateEnvironmentVariableReader() =>
            new EnvironmentVariableReader(new WasiEnvironmentVariableSource(
                new WasiEnvironmentInvoker()));

        internal static IEnvironmentVariableSource CreateInitialEnvironmentVariableSource() =>
            new WasiEnvironmentVariableSource(new WasiEnvironmentInvoker());

        internal static IPlatformMonotonicClock CreateMonotonicClock() =>
            new WasiMonotonicClock(new WasiMonotonicClockSource());

        internal static IPlatformClock CreateClock(
            IPlatformUtcClock utcClock,
            IPlatformMonotonicClock monotonicClock) =>
            new WasiClockBackend(utcClock, monotonicClock);

        internal static WasiPollableReactor CreatePollableReactor()
        {
            var reactor = new WasiPollableReactor(
                new WasiPollableSubscription(),
                new WasiReactorWatcher(),
                new WasiReactorCancellation());
            reactor.RegisterForHostWakeups();
            return reactor;
        }

        internal static IPlatformTimerFactory CreateTimerFactory(
            IPlatformScheduler scheduler) =>
            new PlatformTimerFactory(scheduler);

        internal static IPlatformStreams CreateStreams() => new WasiStreamBackend(
            new WasiOutputStreamHandleSource(),
            new WasiOutputStreamFactory(
                new WasiOutputReadinessSource(new WasiOutputErrorThrower(
                    new WasiOutputErrorResource())),
                new WasiOutputWriterSource(new WasiOutputErrorThrower(
                    new WasiOutputErrorResource())),
                new WasiBlockingOutputWriterSource(
                    new WasiBlockingOutputInvoker(),
                    new WasiOutputErrorThrower(new WasiOutputErrorResource())),
                new WasiOutputResourceSource()));
    }

    // System APIs such as DateTime.UtcNow and Task.Delay are framework-mandated
    // static entry points. This type is their composition boundary, not a
    // service locator: it stores the selected capabilities directly and never
    // resolves a service by type or name.
    internal static class PlatformServices
    {
        private static IPlatformClock? _clock;
        private static ICommandLineArgumentSource? _commandLineArguments;
        private static IPlatformUtcClock? _utcClock;
        private static IPlatformMonotonicClock? _monotonicClock;
        private static WasiPollableReactor? _pollableReactor;
        private static IPlatformScheduler? _scheduler;
        private static IPlatformTimerFactory? _timerFactory;
        private static IPlatformStreams? _streams;
        private static ILocalTimeOffsetResolver? _localTimeOffsets;
        private static bool _localTimeReady;

        // IPlatformClock is retained as a compatibility aggregate for existing
        // CoreLib fixtures. New consumers should request the one-action view.
        internal static IPlatformClock Clock =>
            _clock ??= WasiPlatformServicesComposition.CreateClock(
                UtcClock,
                MonotonicClock);

        internal static ICommandLineArgumentSource CommandLineArguments =>
            _commandLineArguments ??=
                WasiPlatformServicesComposition.CreateCommandLineArgumentSource();

        internal static string[] ReadCommandLineArguments() =>
            CommandLineArguments.Read();

        internal static IPlatformUtcClock UtcClock =>
            _utcClock ??= WasiPlatformServicesComposition.CreateUtcClock();

        internal static IPlatformMonotonicClock MonotonicClock =>
            _monotonicClock ??= WasiPlatformServicesComposition.CreateMonotonicClock();

        internal static IPlatformPollableScheduler PollableScheduler =>
            _pollableReactor ??= WasiPlatformServicesComposition.CreatePollableReactor();

        internal static IPlatformScheduler Scheduler =>
            _scheduler ??= _pollableReactor ??=
                WasiPlatformServicesComposition.CreatePollableReactor();

        internal static IPlatformTimerFactory TimerFactory =>
            _timerFactory ??=
                WasiPlatformServicesComposition.CreateTimerFactory(Scheduler);

        internal static IPlatformStreams Streams =>
            _streams ??= WasiPlatformServicesComposition.CreateStreams();

        internal static ILocalTimeOffsetResolver LocalTimeOffsets =>
            _localTimeOffsets ??=
                WasiPlatformServicesComposition.CreateLocalTimeOffsetResolver();

        internal static void UseLocalTimeOffsetResolver(
            ILocalTimeOffsetResolver resolver)
        {
            _localTimeOffsets = resolver ?? throw new ArgumentNullException();
        }

        internal static void PreflightLocalTime() =>
            WasiPlatformServicesComposition.CreateLocalTimePreflightValidator()
                .Validate();

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
